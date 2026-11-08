// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Editor;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using System.Net;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>Serves the editor: templates, revisions, save, rename, delete and seed restore.</summary>
internal sealed class EditorEndpoint {

    internal const string MessageKey = "editor.message";

    public static async Task HandleAsync(HttpContext context, PageEditor pages, ReservedPaths reserved, IAntiforgery antiforgery) {
        if (context.Request.Method is not ("GET" or "POST")) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status405MethodNotAllowed, new ErrorMessage(
                "editor.method_not_allowed",
                "Not allowed",
                "The editor answers GET and POST only. Follow a link or use one of the editor's buttons.",
                ErrorMessage.DefaultRemedies(context.User.Identity?.IsAuthenticated == true)));
            return;
        }

        if (context.Request.Method == "POST" && context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "editor.form_expired",
                "The editor form expired",
                "The form could not be verified, so nothing was saved. Use your browser's back button to return to your text, reload the editor, then save again.",
                ErrorMessage.DefaultRemedies(context.User.Identity?.IsAuthenticated == true)));
            return;
        }

        (bool commandValid, string? command) = await ReadCommandAsync(context);
        if (!commandValid || !IsAllowedCommand(context.Request.Method, command)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "editor.unknown_command",
                "The editor could not tell what to do",
                "That request did not name an editor action this page understands. Use one of the buttons on the editor page.",
                [new("/edit", "Page list")]));
            return;
        }

        bool bareEditorPath = string.Equals(context.Request.Path.Value, "/edit", StringComparison.Ordinal);
        if (bareEditorPath && context.Request.Method == "GET" && command is null) {
            await WriteListingAsync(context, pages, reserved, antiforgery);
            return;
        }

        if (command == "new") {
            if (context.Request.Method != "POST" || !bareEditorPath) {
                await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                    "editor.new_page",
                    "That is not how a page is created",
                    "Creating a page posts the new-page form from the page list. Open the page list and use \"Create or open\".",
                    [new("/edit", "Page list")]));
                return;
            }

            await NewPageAsync(context, reserved);
            return;
        }

        if (bareEditorPath && (context.Request.Method == "POST" || command is not null)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "editor.needs_page",
                "That action needs a page",
                "The page list has no page to act on. Open the page list and choose a page, or create one with \"Create or open\".",
                [new("/edit", "Page list")]));
            return;
        }

        PagePath path = PagePath.Normalize(context.Request.RouteValues["path"] as string);
        string editorUrl = PageUrls.Editor(path);
        if (!path.IsAddressable) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (context.Request.Method == "POST") {
            await PostCommandAsync(context, pages, reserved, path, command);
            return;
        }

        if (!string.Equals(context.Request.Path.Value, "/edit" + path.Key, StringComparison.Ordinal)) {
            SecurityHeaders.NoStore(context);
            Redirect.Permanent(context, editorUrl + context.Request.QueryString.ToUriComponent());
            return;
        }

        if (reserved.IsReserved(path) && command is not null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        switch (command) {
            case "history":
                await WriteHistoryAsync(context, pages, path);
                return;
            case "revision":
                if (!TryGetRevisionHash(context, out string? hash)) {
                    await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                        "editor.revision_link",
                        "That revision link is not valid",
                        "A revision link needs a 64-character SHA-256 hash. Open the page history and choose a revision from the list.",
                        [new(PageUrls.EditorCommand(path, "history"), "Back to history"), new(PageUrls.Editor(path), "Back to editor")]));
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
                bool assistantEnabled = context.RequestServices?.GetService<IOptions<AssistantOptions>>()?.Value.Enabled == true;
                await WriteEditorAsync(context, pages, antiforgery, assistantEnabled, path);
                return;
            default:
                await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                    "editor.unknown_command",
                    "The editor could not tell what to do",
                    "That request did not name an editor action this page understands. Use one of the buttons on the editor page.",
                    [new(PageUrls.Editor(path), "Back to editor")]));
                return;
        }
    }

    private static async Task WriteListingAsync(HttpContext context, PageEditor pages, ReservedPaths reserved, IAntiforgery antiforgery) {
        IReadOnlyList<PageHeadSummary> heads = await pages.ListAsync(context.RequestAborted);
        PageHeadSummary[] live = [.. heads.Where(head => !head.IsDeleted).OrderBy(head => head.Path.Key, StringComparer.Ordinal)];
        PageHeadSummary[] deleted = [.. heads.Where(head => head.IsDeleted).OrderBy(head => head.Path.Key, StringComparer.Ordinal)];
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        HashSet<string> seeds = new(PageAssetSeeder.SeedPaths().Select(seed => seed.Key), StringComparer.Ordinal);
        PageHeadSummary[] ownPages = [.. live.Where(head => !seeds.Contains(head.Path.Key))];
        PageHeadSummary[] seededPages = [.. live.Where(head => seeds.Contains(head.Path.Key))];

        await ShellDocument.BeginAsync(context, "Pages");
        await context.Response.WriteAsync("<h1>Pages</h1>");
        await WriteMessageAsync(context);
        await context.Response.WriteAsync(AdminNavigation.Nav("edit"));
        await context.Response.WriteAsync($"""
            <form method="post" action="/edit?cmd=new" class="form">
                {AntiforgeryField.Hidden(tokens)}
                <dl>
                    <dt><label for="new-page-path">New page path</label></dt>
                    <dd><input id="new-page-path" name="path" required></dd>
                </dl>
                <button type="submit">Create or open</button>
            </form>
            <h2>Live pages</h2>
            """);

        await WriteLivePagesAsync(context, ownPages, "No live pages.", pages, seeds, tokens, reserved);

        await context.Response.WriteAsync("<h2>Seeded pages</h2>");
        await WriteLivePagesAsync(context, seededPages, "No seeded pages.", pages, seeds, tokens, reserved);

        await context.Response.WriteAsync("<h2>Deleted pages</h2>");
        if (deleted.Length == 0) {
            await context.Response.WriteAsync("<p>No deleted pages.</p>");
        } else {
            await context.Response.WriteAsync("<ul class=\"pages\" role=\"list\">");
            foreach (PageHeadSummary head in deleted) {
                if (!await WriteUnavailableAsync(context, head, reserved)) {
                    continue;
                }

                string key = KeyOf(head);
                string recreateUrl = WebUtility.HtmlEncode(PageUrls.Editor(head.Path));
                string historyUrl = WebUtility.HtmlEncode(PageUrls.EditorCommand(head.Path, "history"));
                string scrubUrl = WebUtility.HtmlEncode(PageUrls.EditorCommand(head.Path, "scrub"));
                string restore = await RestoreFormAsync(pages, head.Path, seeds, tokens, context.RequestAborted);
                await context.Response.WriteAsync($"<li>{key} (history retained) <a href=\"{recreateUrl}\" aria-label=\"Recreate {key}\">Recreate</a> <a href=\"{historyUrl}\" aria-label=\"History of {key}\">History</a> <a href=\"{scrubUrl}\" aria-label=\"Scrub history of {key}\">Scrub history</a> {restore}</li>");
            }
            await context.Response.WriteAsync("</ul>");
        }

        await ShellDocument.EndAsync(context);
    }

    // The owner's pages and the bundled seeds are both live; only the heading and the empty state
    // differ between them.
    private static async Task WriteLivePagesAsync(
        HttpContext context,
        IReadOnlyList<PageHeadSummary> heads,
        string empty,
        PageEditor pages,
        IReadOnlySet<string> seeds,
        AntiforgeryTokenSet tokens,
        ReservedPaths reserved) {
        if (heads.Count == 0) {
            await context.Response.WriteAsync($"<p>{empty}</p>");
            return;
        }

        await context.Response.WriteAsync("<ul class=\"pages\" role=\"list\">");
        foreach (PageHeadSummary head in heads) {
            if (!await WriteUnavailableAsync(context, head, reserved)) {
                continue;
            }

            string key = KeyOf(head);
            string editorUrl = WebUtility.HtmlEncode(PageUrls.Editor(head.Path));
            string historyUrl = WebUtility.HtmlEncode(PageUrls.EditorCommand(head.Path, "history"));
            string deleteUrl = WebUtility.HtmlEncode(PageUrls.EditorCommand(head.Path, "delete"));
            string restore = await RestoreFormAsync(pages, head.Path, seeds, tokens, context.RequestAborted);
            await context.Response.WriteAsync($"<li><a href=\"{WebUtility.HtmlEncode(PageUrls.Page(head.Path))}\">{key}</a> <a href=\"{editorUrl}\" aria-label=\"Edit {key}\">Edit</a> <a href=\"{historyUrl}\" aria-label=\"History of {key}\">History</a> <a href=\"{deleteUrl}\" aria-label=\"Delete {key}\">Delete</a> {restore}</li>");
        }
        await context.Response.WriteAsync("</ul>");
    }

    // A key the site's routes already answer for is not a page the editor can offer, and both the
    // live and the deleted list have to say so the same way.
    private static async Task<bool> WriteUnavailableAsync(
        HttpContext context,
        PageHeadSummary head,
        ReservedPaths reserved) {
        if (head.Path.IsAddressable && !reserved.IsReserved(head.Path)) {
            return true;
        }

        await context.Response.WriteAsync($"<li>{KeyOf(head)}: unavailable</li>");
        return false;
    }

    private static string KeyOf(PageHeadSummary head) => WebUtility.HtmlEncode(head.Path.Key);

    // A seeded row offers the restore; a page that already carries its seed offers it disabled, so
    // the listing shows what could be restored without offering a no-op write. A tombstoned page has
    // no live template to match, so its restore stays enabled.
    private static async Task<string> RestoreFormAsync(
        PageEditor pages,
        PagePath path,
        IReadOnlySet<string> seeds,
        AntiforgeryTokenSet tokens,
        CancellationToken cancellationToken) {
        if (!seeds.Contains(path.Key)) {
            return string.Empty;
        }

        string? seed = await PageAssetSeeder.ReadSeedTemplateAsync(path, cancellationToken);
        if (seed is null) {
            return string.Empty;
        }

        bool matches = await MatchesSeedAsync(pages, path, seed, cancellationToken);
        string key = WebUtility.HtmlEncode(path.Key);
        string action = WebUtility.HtmlEncode(PageUrls.EditorCommand(path, "restore"));
        string disabled = matches ? " disabled" : string.Empty;
        return $"<form method=\"post\" action=\"{action}\">{AntiforgeryField.Hidden(tokens)}<button type=\"submit\" aria-label=\"Restore {key} from its seed\"{disabled}>Restore from seed</button></form>";
    }

    private static async Task<bool> MatchesSeedAsync(PageEditor pages, PagePath path, string seed, CancellationToken cancellationToken) {
        PageTemplate? live = await pages.GetAsync(path, cancellationToken);
        return live is not null && string.Equals(live.Template, seed, StringComparison.Ordinal);
    }

    private static async Task NewPageAsync(HttpContext context, ReservedPaths reserved) {
        SecurityHeaders.NoStore(context);
        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("path", out StringValues value)
            || value.Count != 1
            || string.IsNullOrWhiteSpace(value[0])) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "editor.new_page_path",
                "A page name is required",
                "The new-page form needs a path, for example /notes/first. Nothing was created.",
                [new("/edit", "Page list")]));
            return;
        }

        PagePath path = PagePath.Normalize(value[0]);
        if (!path.IsAddressable || reserved.IsReserved(path)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "editor.new_page_path",
                "That page name cannot be used",
                $"{path} is not a page this site can create: it is either unreadable as a path or one the site answers for itself. Nothing was created.",
                [new("/edit", "Page list")]));
            return;
        }

        Redirect.SeeOther(context, PageUrls.Editor(path));
    }

    private static async Task WriteEditorAsync(
        HttpContext context,
        PageEditor pages,
        IAntiforgery antiforgery,
        bool assistantEnabled,
        PagePath path) {
        // The only page on the site that loads a script, so the only response that widens its policy.
        SecurityHeaders.AllowScripts(context);

        PageTemplate? page = await pages.GetAsync(path, context.RequestAborted);
        PageHeadSummary? head = page is null ? await pages.GetHeadAsync(path, context.RequestAborted) : null;
        bool wasDeleted = head?.IsDeleted == true;
        string template = page?.Template ?? "";
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);

        await ShellDocument.BeginAsync(context, "Editor", stylesheet: "/editor.css", bodyClass: "editor-page");
        await context.Response.WriteAsync($"<h1>Editing {WebUtility.HtmlEncode(path.Key)}</h1>");
        await WriteMessageAsync(context);
        if (wasDeleted) {
            await context.Response.WriteAsync("<p>This page was deleted. Its history is retained; saving here continues that history.</p>");
        }

        if (page?.Image?.Diagnostics is { Count: > 0 } diagnostics) {
            await context.Response.WriteAsync(PageLayout.Diagnostics(DiagnosticLine.From(diagnostics), linkToLines: true));
        }

        List<NavLink> menu = [
            new("/edit", "Page list"),
            new(PageUrls.Page(path), "View file"),
        ];
        if (assistantEnabled) {
            menu.Add(new("/assistant", "Assistant"));
        }
        if (page is not null) {
            menu.Add(new(PageUrls.EditorCommand(path, "history"), "History"));
            menu.Add(new(PageUrls.EditorCommand(path, "delete"), "Delete"));
        } else if (wasDeleted) {
            menu.Add(new(PageUrls.EditorCommand(path, "history"), "History"));
            menu.Add(new(PageUrls.EditorCommand(path, "scrub"), "Scrub retained history"));
        }

        await context.Response.WriteAsync(PageLayout.Nav("Editor navigation", menu, "editor-menu"));

        await context.Response.WriteAsync($"""
            <form method="post" action="?" class="editor-form">
                {AntiforgeryField.Hidden(tokens)}
                <label class="visually-hidden" for="template">Page template</label>
                <div class="editor">
                    {PageLayout.Gutter(TemplateLines.Count(template))}
                    <textarea name="template" id="template" rows="10" cols="50" wrap="off" spellcheck="false">
            {WebUtility.HtmlEncode(template)}</textarea>
                </div>
                <button type="submit" name="cmd" value="save">Save</button>
                <button type="submit" name="cmd" value="save-and-view">Save and view</button>
            </form>
            <form method="post" action="/logout">
                {AntiforgeryField.Hidden(tokens)}
                <button type="submit">Sign out</button>
            </form>
            <script type="module" src="/editor.js"></script>
            """);
        await ShellDocument.EndAsync(context);
    }

    private static async Task WriteDeleteConfirmationAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery, PagePath path) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null || head.IsDeleted) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Delete page");
        await context.Response.WriteAsync($"<h1>Delete {WebUtility.HtmlEncode(path.Key)}?</h1>");
        await context.Response.WriteAsync("<p>This hides the page but retains its template and revision history.</p>");
        await context.Response.WriteAsync($"""
            <form method="post" action="{WebUtility.HtmlEncode(PageUrls.EditorCommand(path, "delete"))}">
                {AntiforgeryField.Hidden(tokens)}
                <button type="submit">Delete page</button>
            </form>
            {PageLayout.Nav("Confirmation navigation", [new(PageUrls.Editor(path), "Cancel")], "editor-menu")}
            """);
        await ShellDocument.EndAsync(context);
    }

    private static async Task WriteScrubConfirmationAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery, PagePath path) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }
        if (!head.IsDeleted) {
            await WriteScrubRequiresDeletedPageAsync(context, path);
            return;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Scrub page history");
        await context.Response.WriteAsync($"<h1>Scrub {WebUtility.HtmlEncode(path.Key)}?</h1>");
        await context.Response.WriteAsync("""
            <p>This irreversibly removes this deleted page and all retained revisions. It cannot be undone and is intended for cases such as committed secrets.</p>
            <p>Scrubbing does not revoke already-running page code or remove browser/CDN copies, exported archives, database backups, or filesystem snapshots. Rotate exposed secrets and clean those copies separately. It cannot promise forensic erasure from SSDs or external copies.</p>
            """);
        await context.Response.WriteAsync($"""
            <form method="post" action="{WebUtility.HtmlEncode(PageUrls.EditorCommand(path, "scrub"))}" class="form">
                {AntiforgeryField.Hidden(tokens)}
                <dl>
                    <dt><label for="confirmation">Type {WebUtility.HtmlEncode(path.Key)} to confirm</label></dt>
                    <dd><input id="confirmation" name="confirmation" required></dd>
                </dl>
                <button type="submit">Permanently scrub</button>
            </form>
            {PageLayout.Nav("Confirmation navigation", [new(PageUrls.Editor(path), "Cancel")], "editor-menu")}
            """);
        await ShellDocument.EndAsync(context);
    }

    private static Task WriteScrubRequiresDeletedPageAsync(HttpContext context, PagePath path) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status409Conflict, new ErrorMessage(
            "editor.scrub_live_page",
            "Delete the page before scrubbing its history",
            $"Scrubbing removes the retained history of a deleted page, so {path.Key} has to be deleted first. Nothing was removed.",
            [new(PageUrls.EditorCommand(path, "delete"), "Delete the page"), new("/edit", "Page list")]));

    private static async Task PostCommandAsync(
        HttpContext context,
        PageEditor pages,
        ReservedPaths reserved,
        PagePath path,
        string? command) {
        if (command is null or "save") {
            SaveOutcome saved = await SaveAsync(context, pages, reserved, path);
            context.Session.SetString(MessageKey, saved.Message);
            Redirect.SeeOther(context, PageUrls.Editor(path));
            return;
        }

        if (command == "save-and-view") {
            SaveOutcome saved = await SaveAsync(context, pages, reserved, path);
            // A page that did not compile cannot be shown; sending the author to a 500 would waste
            // their time when the diagnostics they need are on the editor they just left.
            context.Session.SetString(MessageKey, saved.Viewable
                ? saved.Message
                : saved.Message + " The page cannot be shown until the errors are fixed.");
            Redirect.SeeOther(context, saved.Viewable ? PageUrls.Page(path) : PageUrls.Editor(path));
            return;
        }

        if (reserved.IsReserved(path)) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (command == "delete") {
            bool deleted = await pages.DeleteAsync(path, context.RequestAborted);
            ILogger<EditorEndpoint>? logger = context.RequestServices.GetService<ILogger<EditorEndpoint>>();
            if (deleted) {
                logger?.LogInformation("Deleted {Path}; revision history is retained", path.Key);
            } else {
                logger?.LogDebug("Delete of {Path} was a no-op: it was already missing", path.Key);
            }
            context.Session.SetString(MessageKey, deleted ? $"Deleted {path.Key}; revision history is retained." : "Page was already missing.");
            Redirect.SeeOther(context, "/edit");
            return;
        }

        if (command == "scrub") {
            await ScrubAsync(context, pages, path);
            return;
        }

        if (command == "restore") {
            await RestoreSeedAsync(context, pages, path);
            return;
        }

        await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
            "editor.unknown_command",
            "The editor could not tell what to do",
            "That request did not name an editor action this page understands. Use one of the buttons on the editor page.",
            [new(PageUrls.Editor(path), "Back to editor")]));
    }

    private static async Task RestoreSeedAsync(HttpContext context, PageEditor pages, PagePath path) {
        SecurityHeaders.NoStore(context);
        string? seed = await PageAssetSeeder.ReadSeedTemplateAsync(path, context.RequestAborted);
        if (seed is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        if (await MatchesSeedAsync(pages, path, seed, context.RequestAborted)) {
            context.Session.SetString(MessageKey, $"{path.Key} already matched its seed.");
        } else {
            await pages.SaveAsync(path, seed, context.RequestAborted);
            context.RequestServices.GetService<ILogger<EditorEndpoint>>()
                ?.LogInformation("Restored {Path} from its seed", path.Key);
            context.Session.SetString(MessageKey, $"Restored {path.Key} from its seed.");
        }

        Redirect.SeeOther(context, "/edit");
    }

    private static async Task ScrubAsync(HttpContext context, PageEditor pages, PagePath path) {
        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("confirmation", out StringValues confirmation)
            || confirmation.Count != 1
            || !string.Equals(confirmation[0], path.Key, StringComparison.Ordinal)) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "editor.scrub_confirmation",
                "Scrub was not confirmed",
                $"The confirmation did not match {path.Key}, so nothing was removed. Type the page's key exactly to confirm.",
                [new(PageUrls.EditorCommand(path, "scrub"), "Back to confirmation")]));
            return;
        }

        try {
            PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
            if (head is null) {
                await NotFound.WriteAsync(context, path);
                return;
            }
            if (!head.IsDeleted) {
                await WriteScrubRequiresDeletedPageAsync(context, path);
                return;
            }

            bool scrubbed = await pages.ScrubAsync(path, context.RequestAborted);
            if (!scrubbed) {
                PageHeadSummary? currentHead = await pages.GetHeadAsync(path, context.RequestAborted);
                if (currentHead is null) {
                    await NotFound.WriteAsync(context, path);
                } else {
                    await WriteScrubRequiresDeletedPageAsync(context, path);
                }
                return;
            }

            context.RequestServices.GetService<ILogger<EditorEndpoint>>()
                ?.LogInformation("Scrubbed {Path} and its retained history", path.Key);

            context.Session.SetString(MessageKey, $"Scrubbed {path.Key} and its retained history.");
            Redirect.SeeOther(context, "/edit");
        } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
            throw;
        } catch (Exception exception) {
            if (context.RequestServices is { } services) {
                services.GetService<ILogger<EditorEndpoint>>()?.LogError(exception, "Page scrub cleanup did not complete.");
            }
            context.Session.SetString(MessageKey, "Scrub cleanup did not complete. Check the deleted pages list; if the page remains there, scrub it again.");
            await ShellDocument.BeginAsync(context, "Scrub incomplete", StatusCodes.Status503ServiceUnavailable);
            await context.Response.WriteAsync("<h1>Scrub cleanup did not complete</h1>");
            await context.Response.WriteAsync("<p>Check the deleted pages list; if the page remains there, scrub it again. It may already have been logically removed.</p>");
            await context.Response.WriteAsync(PageLayout.Nav("Recovery navigation", [new("/edit", "Page list")], "editor-menu"));
            await ShellDocument.EndAsync(context);
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

    // A command arrives one of two ways: in the query for a link, or in the form for a submit
    // button. Both are read so either a link or a button can name the action; when both name one
    // and they disagree the request is ambiguous rather than guessed at.
    private static async Task<(bool Valid, string? Command)> ReadCommandAsync(HttpContext context) {
        bool found = false;
        string? command = null;

        if (context.Request.Query.TryGetValue("cmd", out StringValues queryValues)) {
            if (!TrySingleCommand(queryValues, out command)) {
                return (false, null);
            }
            found = true;
        }

        if (context.Request.HasFormContentType) {
            IFormCollection form;
            try {
                form = await context.Request.ReadFormAsync(context.RequestAborted);
            } catch (InvalidDataException exception) {
                // A form the parser will not read is the sender's problem, and the caller answers
                // with a 400 either way; Debug because there is nothing here for anyone to act on.
                context.RequestServices.GetService<ILogger<EditorEndpoint>>()
                    ?.LogDebug(exception, "The editor form for {Path} could not be read", context.Request.Path);
                return (false, null);
            }

            if (form.TryGetValue("cmd", out StringValues formValues)) {
                if (!TrySingleCommand(formValues, out string? formCommand)) {
                    return (false, null);
                }
                if (found && !string.Equals(command, formCommand, StringComparison.Ordinal)) {
                    return (false, null);
                }
                command = formCommand;
                found = true;
            }
        }

        return (true, found ? command : null);
    }

    private static bool TrySingleCommand(StringValues values, out string? command) {
        command = values.Count == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : null;
        return command is not null;
    }

    // A command missing from a POST is a save: that is what the editor's form means, and it is what
    // a browser sends for an implicit submission (the Enter key) that names no button.
    private static bool IsAllowedCommand(string method, string? command) => method switch {
        "GET" => command is null or "history" or "revision" or "delete" or "scrub",
        "POST" => command is null or "save" or "save-and-view" or "new" or "delete" or "scrub" or "restore",
        _ => false,
    };

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

    private static async Task<SaveOutcome> SaveAsync(HttpContext context, PageEditor pages, ReservedPaths reserved, PagePath path) {
        if (reserved.IsReserved(path)) {
            return new SaveOutcome($"{path.Key} is reserved by the site.", Viewable: false);
        }

        if (!context.Request.HasFormContentType
            || !context.Request.Form.TryGetValue("template", out StringValues template)) {
            return new SaveOutcome("A template is required.", Viewable: false);
        }

        if (template.Count != 1) {
            return new SaveOutcome("The template must have exactly one value.", Viewable: false);
        }

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.SaveAsync(path, template.ToString(), context.RequestAborted);
        return diagnostics switch {
            { Count: 0 } => new SaveOutcome("Saved successfully.", Viewable: true),
            _ when diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error) => new SaveOutcome("Saved with errors.", Viewable: false),
            _ => new SaveOutcome("Saved with warnings.", Viewable: true),
        };
    }

    private readonly record struct SaveOutcome(string Message, bool Viewable);

    private static async Task WriteHistoryAsync(HttpContext context, PageEditor pages, PagePath path) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        IReadOnlyList<PageRevisionSummary> revisions = await pages.ListRevisionsAsync(path, context.RequestAborted);
        await ShellDocument.BeginAsync(context, "Page history");
        await context.Response.WriteAsync($"<h1>History for {WebUtility.HtmlEncode(path.Key)}</h1>");
        if (head.IsDeleted) {
            await context.Response.WriteAsync("<p>This page is deleted. Its revisions remain available here.</p>");
        }
        await context.Response.WriteAsync(PageLayout.Nav("Editor navigation", [
            new(PageUrls.Editor(path), "Back to editor"),
            new("/edit", "Page list"),
        ], "editor-menu"));
        if (revisions.Count == 0) {
            await context.Response.WriteAsync("<p>No saved revisions.</p>");
        } else {
            await context.Response.WriteAsync("<table><thead><tr><th scope=\"col\">Saved</th><th scope=\"col\">Revision</th><th scope=\"col\">State</th><th scope=\"col\">Compilation</th><th scope=\"col\">Creation runtime</th></tr></thead><tbody>");
            foreach (PageRevisionSummary revision in revisions) {
                string hash = WebUtility.HtmlEncode(revision.Hash);
                string url = WebUtility.HtmlEncode(PageUrls.EditorRevision(path, revision.Hash));
                string state = revision.IsCurrent ? "<strong>Current</strong>" : string.Empty;
                await context.Response.WriteAsync($"<tr><td>{WebUtility.HtmlEncode(revision.SavedAt.ToString("O", CultureInfo.InvariantCulture))}</td><td><a href=\"{url}\">{hash}</a></td><td>{state}</td><td>{WebUtility.HtmlEncode(revision.ImageStatus)}</td><td>{WebUtility.HtmlEncode(revision.CreationRuntimeId)}</td></tr>");
            }
            await context.Response.WriteAsync("</tbody></table>");
        }
        await ShellDocument.EndAsync(context);
    }

    private static async Task WriteRevisionAsync(HttpContext context, PageEditor pages, PagePath path, string hash) {
        PageRevisionTemplate? revision = await pages.GetRevisionTemplateByHashAsync(hash, context.RequestAborted);
        if (revision is null || revision.Path != path) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        await ShellDocument.BeginAsync(context, "Saved revision", stylesheet: "/editor.css");
        await context.Response.WriteAsync($"<h1>Saved revision for {WebUtility.HtmlEncode(path.Key)}</h1>");
        await context.Response.WriteAsync($"<p>Saved {WebUtility.HtmlEncode(revision.SavedAt.ToString("O", CultureInfo.InvariantCulture))}; {WebUtility.HtmlEncode(revision.ImageStatus)}; creation runtime {WebUtility.HtmlEncode(revision.CreationRuntimeId)}.</p>");
        await context.Response.WriteAsync(PageLayout.Nav("Revision navigation", [
            new(PageUrls.EditorCommand(path, "history"), "Back to history"),
        ], "editor-menu"));
        await context.Response.WriteAsync($"<pre class=\"source\" tabindex=\"0\">{WebUtility.HtmlEncode(revision.Template)}</pre>");
        await ShellDocument.EndAsync(context);
    }
}
