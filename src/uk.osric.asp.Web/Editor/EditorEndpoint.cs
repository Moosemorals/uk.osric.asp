// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Editor;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Net;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

internal sealed class EditorEndpoint {

    internal const string MessageKey = "editor.message";

    public static async Task Handler(HttpContext context, PageEditor pages, ReservedPaths reserved, IAntiforgery antiforgery) {
        context.Response.Headers.CacheControl = "no-store";

        if (context.Request.Method == "POST" && context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (context.Request.Method is not ("GET" or "POST") || !TryGetCommand(context, out string? command)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        bool bareEditorPath = string.Equals(context.Request.Path.Value, "/edit", StringComparison.Ordinal);
        if (bareEditorPath && context.Request.Method == "GET" && command is null) {
            await WriteListingAsync(context, pages, reserved, antiforgery);
            return;
        }

        if (command == "new") {
            if (context.Request.Method != "POST" || !bareEditorPath) {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await NewPageAsync(context, reserved);
            return;
        }

        if (bareEditorPath && (context.Request.Method == "POST" || command is not null)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        PagePath path = PagePath.Normalize(context.Request.RouteValues["path"] as string);
        string editorUrl = EditorUrl(path);
        if (!path.IsAddressable) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (context.Request.Method == "POST") {
            await PostCommandAsync(context, pages, reserved, path, command);
            return;
        }

        if (!string.Equals(context.Request.Path.Value, "/edit" + path.Key, StringComparison.Ordinal)) {
            Redirects.Permanent(context, editorUrl + context.Request.QueryString.ToUriComponent());
            return;
        }

        if (reserved.IsReserved(path) && command is not null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        switch (command) {
            case "generated":
                await WriteGeneratedAsync(context, pages, path);
                return;
            case "history":
                await WriteHistoryAsync(context, pages, path);
                return;
            case "revision":
                if (!TryGetRevisionHash(context, out string? hash)) {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                await WriteRevisionAsync(context, pages, path, hash);
                return;
            case "delete":
                await WriteDeleteConfirmationAsync(context, pages, antiforgery, path);
                return;
            case "scrub":
                await WriteScrubConfirmationAsync(context, pages, antiforgery, path);
                return;
            case null:
                await WriteEditorAsync(context, pages, antiforgery, path);
                return;
            default:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
        }
    }

    private static async Task WriteListingAsync(HttpContext context, PageEditor pages, ReservedPaths reserved, IAntiforgery antiforgery) {
        IReadOnlyList<PageHeadSummary> heads = await pages.ListAsync(context.RequestAborted);
        PageHeadSummary[] live = [.. heads.Where(head => !head.IsDeleted).OrderBy(head => head.Path.Key, StringComparer.Ordinal)];
        PageHeadSummary[] deleted = [.. heads.Where(head => head.IsDeleted).OrderBy(head => head.Path.Key, StringComparer.Ordinal)];
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);

        await BeginHtmlAsync(context, "Pages");
        await context.Response.WriteAsync("<h1>Pages</h1>");
        await WriteMessageAsync(context);
        await context.Response.WriteAsync($"""
            <form method="post" action="/edit?cmd=new">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <label>New page path <input name="path" required></label>
                <button type="submit">Create or open</button>
            </form>
            <h2>Live pages</h2>
            """);

        if (live.Length == 0) {
            await context.Response.WriteAsync("<p>No live pages.</p>");
        } else {
            await context.Response.WriteAsync("<ul>");
            foreach (PageHeadSummary head in live) {
                if (!head.Path.IsAddressable || reserved.IsReserved(head.Path)) {
                    await context.Response.WriteAsync($"<li>{WebUtility.HtmlEncode(head.Path.Key)}: unavailable</li>");
                    continue;
                }

                string path = WebUtility.HtmlEncode(head.Path.Key);
                string editorUrl = WebUtility.HtmlEncode(EditorUrl(head.Path));
                string generatedUrl = WebUtility.HtmlEncode(EditorUrl(head.Path) + "?cmd=generated");
                string historyUrl = WebUtility.HtmlEncode(EditorUrl(head.Path) + "?cmd=history");
                string deleteUrl = WebUtility.HtmlEncode(EditorUrl(head.Path) + "?cmd=delete");
                string scrubUrl = WebUtility.HtmlEncode(EditorUrl(head.Path) + "?cmd=scrub");
                await context.Response.WriteAsync($"<li><a href=\"{WebUtility.HtmlEncode(PageUrl(head.Path))}\">{path}</a> <a href=\"{editorUrl}\">Edit</a> <a href=\"{generatedUrl}\">Generated C#</a> <a href=\"{historyUrl}\">History</a> <a href=\"{deleteUrl}\">Delete</a> <a href=\"{scrubUrl}\">Scrub history</a></li>");
            }
            await context.Response.WriteAsync("</ul>");
        }

        await context.Response.WriteAsync("<h2>Deleted pages</h2>");
        if (deleted.Length == 0) {
            await context.Response.WriteAsync("<p>No deleted pages.</p>");
        } else {
            await context.Response.WriteAsync("<ul>");
            foreach (PageHeadSummary head in deleted) {
                if (!head.Path.IsAddressable || reserved.IsReserved(head.Path)) {
                    await context.Response.WriteAsync($"<li>{WebUtility.HtmlEncode(head.Path.Key)}: unavailable</li>");
                    continue;
                }

                string path = WebUtility.HtmlEncode(head.Path.Key);
                string recreateUrl = WebUtility.HtmlEncode(EditorUrl(head.Path));
                string historyUrl = WebUtility.HtmlEncode(EditorUrl(head.Path) + "?cmd=history");
                string scrubUrl = WebUtility.HtmlEncode(EditorUrl(head.Path) + "?cmd=scrub");
                await context.Response.WriteAsync($"<li>{path} (history retained) <a href=\"{recreateUrl}\">Recreate</a> <a href=\"{historyUrl}\">History</a> <a href=\"{scrubUrl}\">Scrub history</a></li>");
            }
            await context.Response.WriteAsync("</ul>");
        }

        await context.Response.WriteAsync(PageLayout.Footer());
    }

    private static Task NewPageAsync(HttpContext context, ReservedPaths reserved) {
        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("path", out StringValues value)
            || value.Count != 1
            || string.IsNullOrWhiteSpace(value[0])) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        PagePath path = PagePath.Normalize(value[0]);
        if (!path.IsAddressable || reserved.IsReserved(path)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }

        Redirects.SeeOther(context, EditorUrl(path));
        return Task.CompletedTask;
    }

    private static async Task WriteEditorAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery, PagePath path) {
        // The only page on the site that loads a script, so the only response that widens its policy.
        SecurityHeaders.AllowScripts(context);

        StoredPage? page = await pages.GetAsync(path, context.RequestAborted);
        PageHeadSummary? head = page is null ? await pages.GetHeadAsync(path, context.RequestAborted) : null;
        bool wasDeleted = head?.IsDeleted == true;
        string content = page?.Content ?? "";
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);

        await BeginHtmlAsync(context, "Editor", stylesheet: "/editor.css", bodyClass: "editor-page");
        await context.Response.WriteAsync($"<h1>Editing {WebUtility.HtmlEncode(path.Key)}</h1>");
        await WriteMessageAsync(context);
        if (wasDeleted) {
            await context.Response.WriteAsync("<p>This page was deleted. Its history is retained; saving here continues that history.</p>");
        }

        if (page?.Compilation?.Diagnostics is { Count: > 0 } diagnostics) {
            await context.Response.WriteAsync(PageLayout.Diagnostics(diagnostics, linkToLines: true));
        }

        string editorUrl = WebUtility.HtmlEncode(EditorUrl(path));
        await context.Response.WriteAsync($"<nav aria-label=\"Page navigation\"><a href=\"/edit\">Page listing</a> <a href=\"{WebUtility.HtmlEncode(PageUrl(path))}\">Back to file</a></nav>");
        if (page is not null) {
            await context.Response.WriteAsync($"<nav aria-label=\"Page actions\"><a href=\"{editorUrl}?cmd=generated\">Generated C#</a> <a href=\"{editorUrl}?cmd=history\">History</a> <a href=\"{editorUrl}?cmd=delete\">Delete</a> <a href=\"{editorUrl}?cmd=scrub\">Scrub history</a></nav>");
        } else if (wasDeleted) {
            await context.Response.WriteAsync($"<nav aria-label=\"Page actions\"><a href=\"{editorUrl}?cmd=history\">History</a> <a href=\"{editorUrl}?cmd=scrub\">Scrub retained history</a></nav>");
        }

        await context.Response.WriteAsync($"""
            <form method="post" action="?" class="editor-form">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <div class="editor">
                    {PageLayout.Gutter(TemplateLines.Count(content))}
                    <textarea name="content" id="content" rows="10" cols="50" wrap="off" spellcheck="false">
            {WebUtility.HtmlEncode(content)}</textarea>
                </div>
                <button type="submit">Save</button>
            </form>
            <form method="post" action="/logout">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <button type="submit">Sign out</button>
            </form>
            <script type="module" src="/editor.js"></script>
            """);
        await context.Response.WriteAsync(PageLayout.Footer());
    }

    private static async Task WriteGeneratedAsync(HttpContext context, PageEditor pages, PagePath path) {
        StoredPage? page = await pages.GetAsync(path, context.RequestAborted);
        if (page is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        TemplateParseResult generated = TemplateSourceGenerator.Generate(page.Content);
        await BeginHtmlAsync(context, "Generated C#", stylesheet: "/editor.css");
        await context.Response.WriteAsync($"<h1>Generated C# for {WebUtility.HtmlEncode(path.Key)}</h1>");
        await context.Response.WriteAsync("<p>Generated from the saved template by the current runtime; unsaved edits are not included.</p>");
        await context.Response.WriteAsync($"<nav aria-label=\"Editor navigation\"><a href=\"{WebUtility.HtmlEncode(EditorUrl(path))}\">Back to editor</a> <a href=\"/edit\">Page listing</a></nav>");
        if (generated.Source is null) {
            await context.Response.WriteAsync(PageLayout.Diagnostics(generated.Errors));
        } else {
            await context.Response.WriteAsync($"<pre class=\"source\">{WebUtility.HtmlEncode(generated.Source)}</pre>");
        }
        await context.Response.WriteAsync(PageLayout.Footer());
    }

    private static async Task WriteDeleteConfirmationAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery, PagePath path) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null || head.IsDeleted) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await BeginHtmlAsync(context, "Delete page");
        await context.Response.WriteAsync($"<h1>Delete {WebUtility.HtmlEncode(path.Key)}?</h1>");
        await context.Response.WriteAsync("<p>This hides the page but retains its source and revision history.</p>");
        await context.Response.WriteAsync($"""
            <form method="post" action="{WebUtility.HtmlEncode(EditorUrl(path))}?cmd=delete">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <button type="submit">Delete page</button>
            </form>
            <nav aria-label="Confirmation navigation"><a href="{WebUtility.HtmlEncode(EditorUrl(path))}">Cancel</a></nav>
            """);
        await context.Response.WriteAsync(PageLayout.Footer());
    }

    private static async Task WriteScrubConfirmationAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery, PagePath path) {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await BeginHtmlAsync(context, "Scrub page history");
        await context.Response.WriteAsync($"<h1>Scrub {WebUtility.HtmlEncode(path.Key)}?</h1>");
        await context.Response.WriteAsync("""
            <p>This irreversibly removes the live page, if present, and all retained revisions. It cannot be undone and is intended for cases such as committed secrets.</p>
            <p>Scrubbing does not revoke already-running page code or remove browser/CDN copies, exported archives, database backups, or filesystem snapshots. Rotate exposed secrets and clean those copies separately. It cannot promise forensic erasure from SSDs or external copies.</p>
            """);
        await context.Response.WriteAsync($"""
            <form method="post" action="{WebUtility.HtmlEncode(EditorUrl(path))}?cmd=scrub">
                <input type="hidden" name="{WebUtility.HtmlEncode(tokens.FormFieldName)}" value="{WebUtility.HtmlEncode(tokens.RequestToken)}">
                <label>Type {WebUtility.HtmlEncode(path.Key)} to confirm <input name="confirmation" required></label>
                <button type="submit">Permanently scrub</button>
            </form>
            <nav aria-label="Confirmation navigation"><a href="{WebUtility.HtmlEncode(EditorUrl(path))}">Cancel</a></nav>
            """);
        await context.Response.WriteAsync(PageLayout.Footer());
    }

    private static async Task PostCommandAsync(HttpContext context, PageEditor pages, ReservedPaths reserved, PagePath path, string? command) {
        if (command is null) {
            context.Session.SetString(MessageKey, await SaveAsync(context, pages, reserved, path));
            Redirects.SeeOther(context, EditorUrl(path));
            return;
        }

        if (reserved.IsReserved(path)) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (command == "delete") {
            bool deleted = await pages.DeleteAsync(path, context.RequestAborted);
            context.Session.SetString(MessageKey, deleted ? $"Deleted {path.Key}; revision history is retained." : "Page was already missing.");
            Redirects.SeeOther(context, "/edit");
            return;
        }

        if (command == "scrub") {
            await ScrubAsync(context, pages, path);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }

    private static async Task ScrubAsync(HttpContext context, PageEditor pages, PagePath path) {
        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("confirmation", out StringValues confirmation)
            || confirmation.Count != 1
            || !string.Equals(confirmation[0], path.Key, StringComparison.Ordinal)) {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        try {
            bool scrubbed = await pages.ScrubAsync(path, context.RequestAborted);
            context.Session.SetString(MessageKey, scrubbed ? $"Scrubbed {path.Key} and its retained history." : "Page was already missing.");
            Redirects.SeeOther(context, "/edit");
        } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
            throw;
        } catch (Exception exception) {
            if (context.RequestServices is { } services) {
                services.GetService<ILogger<EditorEndpoint>>()?.LogError(exception, "Page scrub cleanup did not complete.");
            }
            context.Session.SetString(MessageKey, "Scrub cleanup did not complete. Retry safely; the page may already be logically removed.");
            await BeginHtmlAsync(context, "Scrub incomplete", StatusCodes.Status503ServiceUnavailable);
            await context.Response.WriteAsync($"<h1>Scrub cleanup did not complete</h1><p>Retry safely; the page may already be logically removed.</p><nav aria-label=\"Recovery navigation\"><a href=\"{WebUtility.HtmlEncode(EditorUrl(path))}?cmd=scrub\">Retry scrub</a> <a href=\"/edit\">Page listing</a></nav>");
            await context.Response.WriteAsync(PageLayout.Footer());
        }
    }

    private static async Task WriteMessageAsync(HttpContext context) {
        string? message = context.Session.GetString(MessageKey);
        if (message is null) {
            return;
        }

        context.Session.Remove(MessageKey);
        if (!string.IsNullOrEmpty(message)) {
            await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(message)}</p>");
        }
    }

    private static async Task BeginHtmlAsync(
        HttpContext context,
        string title,
        int statusCode = StatusCodes.Status200OK,
        string? stylesheet = null,
        string? bodyClass = null) {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.StatusCode = statusCode;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(PageLayout.Header(title, stylesheet, bodyClass));
    }

    private static string EditorUrl(PagePath path) => "/edit" + PageUrl(path);

    private static string PageUrl(PagePath path) => string.Join('/', path.Key.Split('/').Select(Uri.EscapeDataString));

    private static bool TryGetCommand(HttpContext context, out string? command) {
        command = null;
        if (!context.Request.Query.TryGetValue("cmd", out StringValues values)) {
            return true;
        }

        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0])) {
            return false;
        }

        command = values[0];
        return context.Request.Method switch {
            "GET" => command is "generated" or "history" or "revision" or "delete" or "scrub",
            "POST" => command is "new" or "delete" or "scrub",
            _ => false,
        };
    }

    private static bool TryGetRevisionHash(HttpContext context, out string hash) {
        hash = string.Empty;
        if (!context.Request.Query.TryGetValue("hash", out StringValues values)
            || values.Count != 1
            || values[0] is not { Length: 64 } value
            || !value.All(Uri.IsHexDigit)) {
            return false;
        }

        hash = value;
        return true;
    }

    private static async Task<string> SaveAsync(HttpContext context, PageEditor pages, ReservedPaths reserved, PagePath path) {
        if (reserved.IsReserved(path)) {
            return $"{path.Key} is reserved by the site.";
        }

        if (!context.Request.Form.TryGetValue("content", out StringValues content)) {
            return "Content is required.";
        }

        if (content.Count != 1) {
            return "Content must have exactly one value.";
        }

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.SaveAsync(path, content.ToString(), context.RequestAborted);
        return diagnostics switch {
            { Count: 0 } => "Saved successfully.",
            _ when diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error) => "Saved with errors.",
            _ => "Saved with warnings.",
        };
    }

    private static async Task WriteHistoryAsync(HttpContext context, PageEditor pages, PagePath path) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        IReadOnlyList<PageRevisionSummary> revisions = await pages.ListRevisionsAsync(path, context.RequestAborted);
        await BeginHtmlAsync(context, "Page history");
        await context.Response.WriteAsync($"<h1>History for {WebUtility.HtmlEncode(path.Key)}</h1>");
        if (head.IsDeleted) {
            await context.Response.WriteAsync("<p>This page is deleted. Its revisions remain available here.</p>");
        }
        await context.Response.WriteAsync($"<nav aria-label=\"Editor navigation\"><a href=\"{WebUtility.HtmlEncode(EditorUrl(path))}\">Back to editor</a> <a href=\"/edit\">Page listing</a></nav>");
        if (revisions.Count == 0) {
            await context.Response.WriteAsync("<p>No saved revisions.</p>");
        } else {
            await context.Response.WriteAsync("<table><thead><tr><th>Saved</th><th>Revision</th><th>State</th><th>Compilation</th><th>Creation runtime</th></tr></thead><tbody>");
            foreach (PageRevisionSummary revision in revisions) {
                string hash = WebUtility.HtmlEncode(revision.Hash);
                string url = WebUtility.HtmlEncode(EditorUrl(path) + "?cmd=revision&hash=" + Uri.EscapeDataString(revision.Hash));
                string state = revision.IsCurrent ? "<strong>Current</strong>" : string.Empty;
                await context.Response.WriteAsync($"<tr><td>{WebUtility.HtmlEncode(revision.SavedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}</td><td><a href=\"{url}\">{hash}</a></td><td>{state}</td><td>{WebUtility.HtmlEncode(revision.CompilationStatus)}</td><td>{WebUtility.HtmlEncode(revision.CreationRuntimeId)}</td></tr>");
            }
            await context.Response.WriteAsync("</tbody></table>");
        }
        await context.Response.WriteAsync(PageLayout.Footer());
    }

    private static async Task WriteRevisionAsync(HttpContext context, PageEditor pages, PagePath path, string hash) {
        PageRevisionSource? revision = await pages.GetRevisionSourceByHashAsync(hash, context.RequestAborted);
        if (revision is null || revision.Path != path) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        await BeginHtmlAsync(context, "Saved revision", stylesheet: "/editor.css");
        await context.Response.WriteAsync($"<h1>Saved revision for {WebUtility.HtmlEncode(path.Key)}</h1>");
        await context.Response.WriteAsync($"<p>Saved {WebUtility.HtmlEncode(revision.SavedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}; {WebUtility.HtmlEncode(revision.CompilationStatus)}; creation runtime {WebUtility.HtmlEncode(revision.CreationRuntimeId)}.</p>");
        await context.Response.WriteAsync($"<nav aria-label=\"Revision navigation\"><a href=\"{WebUtility.HtmlEncode(EditorUrl(path))}?cmd=history\">Back to history</a></nav>");
        await context.Response.WriteAsync($"<pre class=\"source\">{WebUtility.HtmlEncode(revision.Content)}</pre>");
        await context.Response.WriteAsync(PageLayout.Footer());
    }
}
