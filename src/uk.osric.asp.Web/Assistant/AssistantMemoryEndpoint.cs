// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;

/// <summary>The owner's page for the assistant's memory: list, edit, pin and forget.</summary>
/// <remarks>
/// The page is shell code and one document per response, like the editor and <c>/status</c>: no
/// script, every mutating form carrying the antiforgery pair, and every write going through
/// <see cref="IMemoryStore.EditAsync"/> or <see cref="IMemoryStore.ForgetAsync"/> rather than through
/// a second set of rules (DEC-200). The owner is always the signed-in account, so a wrong owner and a
/// missing memory answer the same way.
/// </remarks>
internal static class AssistantMemoryEndpoint {

    /// <summary>The page's own route, which the assistant's navigation links to.</summary>
    public const string Path = "/assistant/memory";

    /// <summary>Lists the owner's memories, grouped by kind and pinned first inside a kind.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="memories">The store the memories are read from.</param>
    /// <param name="antiforgery">Mints the token every row's form carries.</param>
    /// <param name="options">The owner's cap, for the count.</param>
    public static async Task ListAsync(
        HttpContext context,
        IMemoryStore memories,
        IAntiforgery antiforgery,
        AssistantOptions options) {
        ArgumentNullException.ThrowIfNull(context);

        string? owner = Owner(context);
        if (owner is null) {
            await WriteNotFoundAsync(context, "That memory does not exist");
            return;
        }

        await WriteListAsync(context, owner, memories, antiforgery, options, state: null, StatusCodes.Status200OK);
    }

    /// <summary>Saves one memory's content, kind and pin, refusing a stale version.</summary>
    /// <remarks>
    /// A refusal re-renders the list rather than answering with a bare error page, because the
    /// textarea is the only copy of what the owner typed: a `400` keeps the submitted text and a
    /// `409` shows the content the assistant left behind (DEC-203).
    /// </remarks>
    /// <param name="context">The response being written.</param>
    /// <param name="id">The memory being saved.</param>
    /// <param name="memories">The store the memory is read from and written to.</param>
    /// <param name="antiforgery">Mints the token the re-rendered forms carry.</param>
    /// <param name="options">The owner's cap, for the count and the row's cap.</param>
    public static async Task SaveAsync(
        HttpContext context,
        long id,
        IMemoryStore memories,
        IAntiforgery antiforgery,
        AssistantOptions options) {
        ArgumentNullException.ThrowIfNull(context);

        SecurityHeaders.NoStore(context);
        if (!IsPostValid(context)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "assistant.form_expired",
                "The memory was not saved",
                "The form could not be verified, so nothing was saved. Your browser's back button returns to the text, then reload this page and try again.",
                [new(Path, "Memory")]));
            return;
        }

        string? owner = Owner(context);
        if (owner is null) {
            await WriteNotFoundAsync(context, "The memory was not saved");
            return;
        }

        if (!TryReadSubmission(context, out MemoryKind kind, out string? content, out bool pinned, out int expectedVersion)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "assistant.memory_invalid",
                "The memory was not saved",
                "That request did not carry a complete memory. Reload this page and try again.",
                [new(Path, "Memory")]));
            return;
        }

        if (await memories.GetAsync(owner, id, context.RequestAborted) is null) {
            await WriteNotFoundAsync(context, "The memory was not saved");
            return;
        }

        MemoryWriteResult saved = await memories.EditAsync(
            owner, id, expectedVersion, kind, content!, pinned, context.RequestAborted);
        if (saved.IsSuccess) {
            Redirect.SeeOther(context, Path);
            return;
        }

        // Which refusal it is decides what the row shows: a version the assistant has overtaken is
        // answered with what is stored now, and anything else keeps the text the owner submitted.
        MemoryCandidate? current = await memories.GetAsync(owner, id, context.RequestAborted);
        if (current is null) {
            await WriteNotFoundAsync(context, "The memory was not saved");
            return;
        }

        bool stale = current.Version != expectedVersion;
        MemoryRowState state = stale
            ? new MemoryRowState(current.Id, current.Content, current.Kind, current.Pinned, current.Version, saved.Error!)
            : new MemoryRowState(id, content!, kind, pinned, current.Version, saved.Error!);
        await WriteListAsync(context, owner, memories, antiforgery, options, state,
            stale ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
    }

    /// <summary>Asks before a memory is forgotten.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="id">The memory the confirmation is about.</param>
    /// <param name="memories">The store the memory is read from.</param>
    /// <param name="antiforgery">Mints the token the delete's form carries.</param>
    public static async Task ForgetConfirmationAsync(
        HttpContext context,
        long id,
        IMemoryStore memories,
        IAntiforgery antiforgery) {
        ArgumentNullException.ThrowIfNull(context);

        string? owner = Owner(context);
        MemoryCandidate? memory = owner is null ? null : await memories.GetAsync(owner, id, context.RequestAborted);
        if (memory is null) {
            await WriteNotFoundAsync(context, "That memory does not exist");
            return;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Forget memory", SiteNavigation.For(context),
            stylesheets: ["/assistant.css"], bodyClass: "assistant-page");
        await context.Response.WriteAsync("<h1>Forget memory</h1>");
        await context.Response.WriteAsync(
            $"<p class=\"assistant-memory-content\">{Encode(memory.Content)}</p>");
        await context.Response.WriteAsync(
            $"<p>Written {Encode(Date(memory.Written))}, {Encode(KindName(memory.Kind))}. Forget it?</p>");
        await context.Response.WriteAsync("""
            <p>Forgetting deletes the memory, its history and its search entry. It does not reach backups,
            snapshots or the operator's own copies of the database, so this is the same best-effort erasure
            a page scrub gets.</p>
            """);
        await context.Response.WriteAsync($"""
            <form method="post" action="{Path}/{id.ToString(CultureInfo.InvariantCulture)}/forget" class="form">
                {AntiforgeryField.Hidden(tokens)}
                <button type="submit">Forget this memory</button>
            </form>
            """);
        await context.Response.WriteAsync(PageDocument.Nav("Memory navigation", [
            new(Path, "Cancel"),
        ]));
        await ShellDocument.EndAsync(context);
    }

    /// <summary>Deletes a memory the owner has confirmed.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="id">The memory to forget.</param>
    /// <param name="memories">The store the memory is deleted from.</param>
    public static async Task ForgetAsync(HttpContext context, long id, IMemoryStore memories) {
        ArgumentNullException.ThrowIfNull(context);

        SecurityHeaders.NoStore(context);
        if (!IsPostValid(context)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "assistant.form_expired",
                "The memory was not forgotten",
                "The form could not be verified, so nothing was forgotten. Reload this page and try again.",
                [new(Path, "Memory")]));
            return;
        }

        string? owner = Owner(context);
        if (owner is null || !await memories.ForgetAsync(owner, id, context.RequestAborted)) {
            await WriteNotFoundAsync(context, "That memory does not exist");
            return;
        }

        Redirect.SeeOther(context, Path);
    }

    // One row whose form is being re-rendered after a refusal: what to put in its fields, which
    // version the next save has to carry, and the sentence that says why.
    private sealed record MemoryRowState(long Id, string Content, MemoryKind Kind, bool Pinned, int Version, string Error);

    private static async Task WriteListAsync(
        HttpContext context,
        string owner,
        IMemoryStore memories,
        IAntiforgery antiforgery,
        AssistantOptions options,
        MemoryRowState? state,
        int statusCode) {
        IReadOnlyList<MemoryCandidate> held = await memories.ListAsync(owner, context.RequestAborted);
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);

        await ShellDocument.BeginAsync(context, "Memory", SiteNavigation.For(context),
            statusCode: statusCode, stylesheets: ["/assistant.css"], bodyClass: "assistant-page");
        await context.Response.WriteAsync("<h1>Memory</h1>");
        await context.Response.WriteAsync(PageDocument.Nav("Assistant navigation", [
            new("/assistant", "Conversations"),
            new(Path, "Memory", true),
            new("/edit", "Page list"),
        ]));
        await context.Response.WriteAsync(Count(held.Count, options.MaxMemoriesPerOwner));

        if (held.Count == 0) {
            await context.Response.WriteAsync("""
                <p>The assistant has not remembered anything yet. It writes a memory with the
                <code>remember</code> tool, and the conversation that wrote it shows the call.</p>
                """);
        } else {
            await context.Response.WriteAsync("<div class=\"assistant-memories\">");
            foreach (MemoryGroup group in MemoryListing.Group(held)) {
                await context.Response.WriteAsync("<section>");
                await context.Response.WriteAsync($"<h2>{Encode(Heading(group.Kind))}</h2>");
                foreach (MemoryCandidate memory in group.Memories) {
                    await WriteRowAsync(context, memory, tokens, options, state);
                }
                await context.Response.WriteAsync("</section>");
            }
            await context.Response.WriteAsync("</div>");
        }

        await ShellDocument.EndAsync(context);
    }

    private static async Task WriteRowAsync(
        HttpContext context,
        MemoryCandidate memory,
        AntiforgeryTokenSet tokens,
        AssistantOptions options,
        MemoryRowState? state) {
        bool refused = state is not null && state.Id == memory.Id;
        string id = memory.Id.ToString(CultureInfo.InvariantCulture);
        string content = refused ? state!.Content : memory.Content;
        MemoryKind kind = refused ? state!.Kind : memory.Kind;
        bool pinned = refused ? state!.Pinned : memory.Pinned;
        int version = refused ? state!.Version : memory.Version;

        await context.Response.WriteAsync($"<article class=\"assistant-memory\" id=\"memory-{id}\">");
        await context.Response.WriteAsync($"<p class=\"assistant-memory-meta\">{Meta(memory, pinned)}{Provenance(memory)}</p>");
        if (refused) {
            await context.Response.WriteAsync(
                $"<p class=\"assistant-memory-error\" role=\"alert\">{Encode(state!.Error)}</p>");
        }

        await context.Response.WriteAsync($"""
            <form method="post" action="{Path}/{id}" class="form">
                {AntiforgeryField.Hidden(tokens)}
                <input type="hidden" name="expectedVersion" value="{version.ToString(CultureInfo.InvariantCulture)}">
                <fieldset>
                    <legend><label class="visually-hidden" for="memory-{id}-content">Content of memory {id}</label>Memory {id}</legend>
                    <textarea id="memory-{id}-content" name="content" maxlength="{options.MaxMemoryChars.ToString(CultureInfo.InvariantCulture)}" rows="4">{Encode(content)}</textarea>
                </fieldset>
                <dl>
                    <dt><label for="memory-{id}-kind">Kind</label></dt>
                    <dd><select id="memory-{id}-kind" name="kind">{Kinds(kind)}</select></dd>
                    <dt><label for="memory-{id}-pinned">Pinned</label></dt>
                    <dd><input type="checkbox" id="memory-{id}-pinned" name="pinned" value="on"{(pinned ? " checked" : "")}></dd>
                </dl>
                <button type="submit">Save memory</button>
            </form>
            """);
        await context.Response.WriteAsync(
            $"<p class=\"assistant-memory-forget\"><a href=\"{Path}/{id}/forget\">Forget this memory</a></p>");
        await context.Response.WriteAsync("</article>");
    }

    // Written, used and pinned: the three facts that decide whether a memory is worth keeping. The
    // content itself is the textarea, so the page shows it once.
    private static string Meta(MemoryCandidate memory, bool pinned) {
        string written = Date(memory.Written);
        string used = memory.RecallCount == 0
            ? "never used"
            : $"used {memory.RecallCount.ToString(CultureInfo.InvariantCulture)} time{(memory.RecallCount == 1 ? "" : "s")}"
                + (memory.LastUsed is { } last ? $", last on {Encode(Date(last))}" : "");
        string badge = pinned ? "<span class=\"assistant-memory-pinned\">Pinned</span> " : "";
        return $"{badge}Written {Encode(written)}. {Encode(used)}.";
    }

    // Provenance is a link while the conversation exists and the sentence the plan named after it is
    // deleted: the delete nulls the column rather than the memory (DEC-184).
    private static string Provenance(MemoryCandidate memory) => memory.SourceConversationId is { } conversation
        ? $" From <a href=\"/assistant/{conversation.ToString(CultureInfo.InvariantCulture)}\">conversation {conversation.ToString(CultureInfo.InvariantCulture)}</a>."
        : " From a deleted conversation.";

    private static string Count(int count, int maximum) {
        string of = $"{count.ToString(CultureInfo.InvariantCulture)} of {maximum.ToString(CultureInfo.InvariantCulture)} memories";
        return count >= maximum
            ? $"<p class=\"assistant-memory-count\">{of}, which is the cap: the assistant cannot remember anything new until one is forgotten.</p>"
            : $"<p class=\"assistant-memory-count\">{of}.</p>";
    }

    private static string Kinds(MemoryKind current) => string.Concat(MemoryKinds.All.Select(kind =>
        $"<option value=\"{Encode(MemoryKinds.Name(kind))}\"{(kind == current ? " selected" : "")}>{Encode(KindName(kind))}</option>"));

    private static string KindName(MemoryKind kind) => kind switch {
        MemoryKind.Preference => "preference",
        MemoryKind.Fact => "fact",
        MemoryKind.Feedback => "feedback",
        MemoryKind.Reference => "reference",
        _ => "memory",
    };

    private static string Heading(MemoryKind kind) => kind switch {
        MemoryKind.Preference => "Preferences",
        MemoryKind.Fact => "Facts",
        MemoryKind.Feedback => "Feedback",
        MemoryKind.Reference => "References",
        _ => "Memories",
    };

    private static string Date(DateTimeOffset when) =>
        when.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The form's shape only: the store is still the authority on the content, its cap and its secrets.
    private static bool TryReadSubmission(
        HttpContext context,
        out MemoryKind kind,
        out string? content,
        out bool pinned,
        out int expectedVersion) {
        kind = default;
        content = null;
        pinned = false;
        expectedVersion = 0;

        if (!context.Request.HasFormContentType) {
            return false;
        }

        Microsoft.Extensions.Primitives.StringValues values = context.Request.Form["content"];
        Microsoft.Extensions.Primitives.StringValues kinds = context.Request.Form["kind"];
        Microsoft.Extensions.Primitives.StringValues versions = context.Request.Form["expectedVersion"];
        if (values.Count != 1 || kinds.Count != 1 || versions.Count != 1
            || !MemoryKinds.TryParse(kinds[0], out kind)
            || !int.TryParse(versions[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedVersion)) {
            return false;
        }

        content = values[0] ?? string.Empty;
        pinned = context.Request.Form.ContainsKey("pinned");
        return true;
    }

    private static string? Owner(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static bool IsPostValid(HttpContext context) =>
        context.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: false };

    private static Task WriteNotFoundAsync(HttpContext context, string title) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound, new ErrorMessage(
            "assistant.memory_not_found",
            title,
            "It may have been forgotten, or it belongs to another account.",
            [new(Path, "Memory")]));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
