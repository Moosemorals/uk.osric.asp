// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Editor;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;
// The page contract has a diagnostic value of its own, so this file names the compiler's.
using PageDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;
using PageDiagnosticSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;

/// <summary>Serves the editor: templates, revisions, save, rename, delete and seed restore.</summary>
internal sealed class EditorEndpoint {

    internal const string MessageKey = "editor.message";

    // The flash is a session key like any other, so it is written under the path of the endpoint
    // that will read it: the listing at /edit, the editor view at /edit/{path}. Both are reserved,
    // so no page can occupy either.
    private const string ListingPath = "/edit";

    public static async Task HandleAsync(
        HttpContext context,
        PageEditor pages,
        DiffRenderer diff,
        ReservedPaths reserved,
        IAntiforgery antiforgery,
        IPageSessionStore sessions,
        PageSessionOptions sessionOptions,
        PageSessionCookieOptions sessionCookie) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(sessionOptions);
        ArgumentNullException.ThrowIfNull(sessionCookie);

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

        // The flash lives in the visitor's session, which is resolved once so the write and the
        // render that follows it cannot name different sessions.
        PageSessionRequest session = await PageSessionResolver.ResolveAsync(
            context, sessions, sessionCookie, MapUser(context), context.RequestAborted);
        Flash flash = new(session, sessions, sessionOptions, sessionCookie);

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
            await WriteListingAsync(context, pages, reserved, antiforgery, flash);
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
            await PostCommandAsync(context, pages, reserved, path, command, flash);
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
                        "A revision link needs a 64-character lowercase hexadecimal hash. Open the page history and choose a revision from the list.",
                        [new(PageUrls.EditorCommand(path, "history"), "Back to history"), new(PageUrls.Editor(path), "Back to editor")]));
                    return;
                }
                await WriteRevisionAsync(context, pages, path, hash);
                return;
            case "diff":
                if (!TryGetRevisionPair(context, out string fromHash, out string toHash)) {
                    await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                        "editor.diff_link",
                        "That comparison link is not valid",
                        "A comparison link needs two 64-character lowercase hexadecimal hashes, one in from and one in to. Open the page history and choose two revisions.",
                        [new(PageUrls.EditorCommand(path, "history"), "Back to history"), new(PageUrls.Editor(path), "Back to editor")]));
                    return;
                }
                await WriteDiffAsync(context, pages, diff, path, fromHash, toHash);
                return;
            case "install":
                await WriteInstallConfirmationAsync(context, pages, diff, antiforgery, path);
                return;
            case "restore":
                if (!TryGetRevisionHash(context, out string? restoreHash)) {
                    await WriteInvalidRestoreLinkAsync(context, path);
                    return;
                }
                await WriteRestoreConfirmationAsync(context, pages, diff, antiforgery, path, restoreHash);
                return;
            case "delete":
                await WriteDeleteConfirmationAsync(context, pages, antiforgery, path);
                return;
            case "scrub":
                await WriteScrubConfirmationAsync(context, pages, antiforgery, path);
                return;
            case null:
                bool assistantEnabled = context.RequestServices?.GetService<IOptions<AssistantOptions>>()?.Value.Enabled == true;
                await WriteEditorAsync(context, pages, antiforgery, assistantEnabled, path, flash);
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

    private static async Task WriteListingAsync(
        HttpContext context,
        PageEditor pages,
        ReservedPaths reserved,
        IAntiforgery antiforgery,
        Flash flash) {
        IReadOnlyList<PageListing> listings = await pages.ListAsync(context.RequestAborted);
        PageListing[] live = [.. listings.Where(row => !row.IsDeleted).OrderBy(row => row.Path.Key, StringComparer.Ordinal)];
        PageListing[] deleted = [.. listings.Where(row => row.IsDeleted).OrderBy(row => row.Path.Key, StringComparer.Ordinal)];
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        HashSet<string> seeds = new(PageAssetSeeder.SeedPaths().Select(seed => seed.Key), StringComparer.Ordinal);
        PageListing[] ownPages = [.. live.Where(row => !seeds.Contains(row.Path.Key))];
        PageListing[] seededPages = [.. live.Where(row => seeds.Contains(row.Path.Key))];

        await ShellDocument.BeginAsync(context, "Pages",
            SiteNavigation.For(context));
        await context.Response.WriteAsync("<h1>Pages</h1>");
        await flash.ShowAsync(context, ListingPath, context.RequestAborted);
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

        await WritePageTableAsync(context, ownPages, "No live pages.", pages, seeds, reserved, deleted: false);

        await context.Response.WriteAsync("<h2>Seeded pages</h2>");
        await WritePageTableAsync(context, seededPages, "No seeded pages.", pages, seeds, reserved, deleted: false);

        await context.Response.WriteAsync("<h2>Deleted pages</h2>");
        await WritePageTableAsync(context, deleted, "No deleted pages.", pages, seeds, reserved, deleted: true);

        await ShellDocument.EndAsync(context);
    }

    // The owner's pages, the bundled seeds and the tombstones are all one table: the heading and the
    // empty state are what differ, plus the action a deleted row offers in place of a delete.
    private static async Task WritePageTableAsync(
        HttpContext context,
        IReadOnlyList<PageListing> rows,
        string empty,
        PageEditor pages,
        IReadOnlySet<string> seeds,
        ReservedPaths reserved,
        bool deleted) {
        if (rows.Count == 0) {
            await context.Response.WriteAsync($"<p>{empty}</p>");
            return;
        }

        await context.Response.WriteAsync("""
            <table class="pages">
                <thead><tr><th scope="col">Page</th><th scope="col">State</th><th scope="col">Actions</th></tr></thead>
                <tbody>
            """);

        foreach (PageListing row in rows) {
            string key = WebUtility.HtmlEncode(row.Path.Key);
            string state = $"<td class=\"page-state {row.State.Class()}\">{WebUtility.HtmlEncode(row.State.Describe())}</td>";
            string install = await InstallActionAsync(pages, row.Path, seeds, context.RequestAborted);

            if (!row.Path.IsAddressable || reserved.IsReserved(row.Path)) {
                // A key the site's routes already answer for is not a page the editor can offer, and
                // both the live and the deleted table have to say so the same way.
                await context.Response.WriteAsync($"<tr><th scope=\"row\">{key}: unavailable</th>{state}<td></td></tr>");
                continue;
            }

            string page = deleted
                ? $"<th scope=\"row\">{key}</th>"
                : $"<th scope=\"row\"><a href=\"{WebUtility.HtmlEncode(PageUrls.Page(row.Path))}\" aria-label=\"View {key}\">{key}</a></th>";
            await context.Response.WriteAsync($"<tr>{page}{state}<td class=\"page-actions\">{ActionsOf(row, key, deleted)} {install}</td></tr>");
        }

        await context.Response.WriteAsync("</tbody></table>");
    }

    private static string ActionsOf(PageListing row, string key, bool deleted) {
        string historyUrl = WebUtility.HtmlEncode(PageUrls.EditorCommand(row.Path, "history"));
        string history = $"<a href=\"{historyUrl}\" aria-label=\"History of {key}\">History</a>";

        if (deleted) {
            string scrubUrl = WebUtility.HtmlEncode(PageUrls.EditorCommand(row.Path, "scrub"));
            return $"{history} <a href=\"{scrubUrl}\" aria-label=\"Scrub history of {key}\">Scrub history</a>";
        }

        string editorUrl = WebUtility.HtmlEncode(PageUrls.Editor(row.Path));
        string deleteUrl = WebUtility.HtmlEncode(PageUrls.EditorCommand(row.Path, "delete"));
        return $"<a href=\"{editorUrl}\" aria-label=\"Edit {key}\">Edit</a> {history} "
            + $"<a href=\"{deleteUrl}\" aria-label=\"Delete {key}\">Delete</a>";
    }

    // Installing a seed opens its confirmation, so the listing's action is a link like every other
    // one there. A page that already carries its seed renders nothing: a link that cannot act is
    // worse than an absent one, and the confirmation is the backstop for a URL typed by hand.
    private static async Task<string> InstallActionAsync(
        PageEditor pages,
        PagePath path,
        IReadOnlySet<string> seeds,
        CancellationToken cancellationToken) {
        if (!seeds.Contains(path.Key)) {
            return string.Empty;
        }

        string? seed = await PageAssetSeeder.ReadSeedTemplateAsync(path, cancellationToken);
        if (seed is null || await MatchesSeedAsync(pages, path, seed, cancellationToken)) {
            return string.Empty;
        }

        string key = WebUtility.HtmlEncode(path.Key);
        string url = WebUtility.HtmlEncode(PageUrls.EditorInstall(path));
        return $"<a href=\"{url}\" aria-label=\"Install {key} from its seed\">Install from seed</a>";
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
        PagePath path,
        Flash flash) {
        // The editor loads a module, so it is one of the responses that widens the policy; the assistant,
        // the admin views and a page that calls ctx.AllowScripts() do the same.
        SecurityHeaders.AllowScripts(context);

        PageTemplate? page = await pages.GetAsync(path, context.RequestAborted);
        PageHeadSummary? head = page is null ? await pages.GetHeadAsync(path, context.RequestAborted) : null;
        bool wasDeleted = head?.IsDeleted == true;
        string template = page?.Template ?? "";
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);

        await ShellDocument.BeginAsync(context, "Editor",
            SiteNavigation.For(context), stylesheets: ["/editor.css"], bodyClass: "editor-page");
        await context.Response.WriteAsync($"<h1>Editing {WebUtility.HtmlEncode(path.Key)}</h1>");
        await flash.ShowAsync(context, SessionPathFor(path), context.RequestAborted);
        if (wasDeleted) {
            // The key has a past, and the author can act on it here: saving revives the page and
            // continues its history, and until then the old links answer "not found".
            string deletedKey = WebUtility.HtmlEncode(path.Key);
            await context.Response.WriteAsync($"<p><strong>{deletedKey} was deleted.</strong> Its history is retained, and saving here brings the page back and continues that history. Until you save, links to {deletedKey} answer &quot;not found&quot;.</p>");
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
        menu.Add(new("/help", "Help"));

        await context.Response.WriteAsync(PageDocument.Nav("Editor navigation", menu, "editor-menu"));

        string script = await ShellDocument.AssetUrlAsync(context, "/editor.js", context.RequestAborted);
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
            <script type="module" src="{WebUtility.HtmlEncode(script)}"></script>
            """);
        await ShellDocument.EndAsync(context);
    }

    // Installing a seed and restoring a revision are one page with a different dictionary: what will
    // be saved, the diff from the live source, and a button — or, when the source is already what the
    // page holds, the diff's own "No changes." and no form at all. Neither view compiles or writes.
    private static async Task WriteInstallConfirmationAsync(
        HttpContext context,
        PageEditor pages,
        DiffRenderer diff,
        IAntiforgery antiforgery,
        PagePath path) {
        string? seed = await PageAssetSeeder.ReadSeedTemplateAsync(path, context.RequestAborted);
        if (seed is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        PageTemplate? live = await pages.GetAsync(path, context.RequestAborted);
        PageHeadSummary? head = live is null ? await pages.GetHeadAsync(path, context.RequestAborted) : null;
        bool changes = !string.Equals(live?.Template ?? "", seed, StringComparison.Ordinal);
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);

        await ShellDocument.BeginAsync(context, "Install the seed",
            SiteNavigation.For(context), stylesheets: ["/editor.css"]);
        await context.Response.WriteAsync($"<h1>Install the seed over {WebUtility.HtmlEncode(path.Key)}?</h1>");
        await context.Response.WriteAsync("<p>The bundled seed will be saved as a new revision and compiled by this build.</p>");
        if (head?.IsDeleted == true) {
            await context.Response.WriteAsync("<p>This page is deleted. Installing brings it back and clears the tombstone.</p>");
        }

        await context.Response.WriteAsync(diff.ToHtml(live?.Template ?? "", seed));

        if (changes) {
            await context.Response.WriteAsync($"""
                <form method="post" action="{WebUtility.HtmlEncode(PageUrls.EditorInstall(path))}" class="form">
                    {AntiforgeryField.Hidden(tokens)}
                    <button type="submit">Install the seed</button>
                </form>
                """);
        } else {
            await context.Response.WriteAsync("<p>This page already holds its seed, so there is nothing to install.</p>");
        }

        await context.Response.WriteAsync(PageDocument.Nav("Confirmation navigation", [new("/edit", "Cancel")], "editor-menu"));
        await ShellDocument.EndAsync(context);
    }

    private static async Task WriteRestoreConfirmationAsync(
        HttpContext context,
        PageEditor pages,
        DiffRenderer diff,
        IAntiforgery antiforgery,
        PagePath path,
        string hash) {
        if (await pages.GetHeadAsync(path, context.RequestAborted) is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        PageRevisionTemplate? revision = await pages.GetRevisionTemplateByHashAsync(hash, context.RequestAborted);
        if (revision is null || revision.Path != path) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        PageTemplate? live = await pages.GetAsync(path, context.RequestAborted);
        bool changes = !string.Equals(live?.Template ?? "", revision.Template, StringComparison.Ordinal);
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        string savedAt = WebUtility.HtmlEncode(revision.SavedAt.ToString("O", CultureInfo.InvariantCulture));

        await ShellDocument.BeginAsync(context, "Restore a revision",
            SiteNavigation.For(context), stylesheets: ["/editor.css"]);
        await context.Response.WriteAsync($"<h1>Restore revision {ShortHash(revision.Hash)} of {WebUtility.HtmlEncode(path.Key)}?</h1>");
        await context.Response.WriteAsync($"<p>Revision {WebUtility.HtmlEncode(revision.Hash)} was saved <time datetime=\"{savedAt}\">{savedAt}</time>; {WebUtility.HtmlEncode(revision.ImageState.Describe())}; creation runtime {WebUtility.HtmlEncode(revision.CreationRuntimeId)}. Its source will be saved as a new revision and compiled by this build.</p>");
        if (live is null) {
            await context.Response.WriteAsync("<p>This page is deleted. Restoring brings it back and clears the tombstone.</p>");
        }

        await context.Response.WriteAsync(diff.ToHtml(live?.Template ?? "", revision.Template));

        if (changes) {
            await context.Response.WriteAsync($"""
                <form method="post" action="{WebUtility.HtmlEncode(PageUrls.EditorRestore(path, revision.Hash))}" class="form">
                    {AntiforgeryField.Hidden(tokens)}
                    <button type="submit">Restore this revision</button>
                </form>
                """);
        } else {
            await context.Response.WriteAsync("<p>This revision has the source the page already holds, so there is nothing to restore.</p>");
        }

        await context.Response.WriteAsync(PageDocument.Nav("Confirmation navigation", [
            new(PageUrls.EditorCommand(path, "history"), "Cancel"),
        ], "editor-menu"));
        await ShellDocument.EndAsync(context);
    }

    private static async Task WriteDeleteConfirmationAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery, PagePath path) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null || head.IsDeleted) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        await ShellDocument.BeginAsync(context, "Delete page",
            SiteNavigation.For(context));
        await context.Response.WriteAsync($"<h1>Delete {WebUtility.HtmlEncode(path.Key)}?</h1>");
        await context.Response.WriteAsync("<p>This hides the page but retains its template and revision history.</p>");
        await context.Response.WriteAsync($"""
            <form method="post" action="{WebUtility.HtmlEncode(PageUrls.EditorCommand(path, "delete"))}">
                {AntiforgeryField.Hidden(tokens)}
                <button type="submit">Delete page</button>
            </form>
            {PageDocument.Nav("Confirmation navigation", [new(PageUrls.Editor(path), "Cancel")], "editor-menu")}
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
        await ShellDocument.BeginAsync(context, "Scrub page history",
            SiteNavigation.For(context));
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
            {PageDocument.Nav("Confirmation navigation", [new(PageUrls.Editor(path), "Cancel")], "editor-menu")}
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
        string? command,
        Flash flash) {
        if (command is null or "save") {
            SaveOutcome saved = await SaveAsync(context, pages, reserved, path);
            await flash.WriteAsync(context, SessionPathFor(path), saved.Message, context.RequestAborted);
            Redirect.SeeOther(context, PageUrls.Editor(path));
            return;
        }

        if (command == "save-and-view") {
            SaveOutcome saved = await SaveAsync(context, pages, reserved, path);
            // The message used to be left for the next editor render, which is a place the author
            // was not always sent; save-and-view shows its diagnostics on the page it lands on.
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
            await flash.WriteAsync(
                context,
                ListingPath,
                deleted ? $"Deleted {path.Key}; revision history is retained." : "Page was already missing.",
                context.RequestAborted);
            Redirect.SeeOther(context, "/edit");
            return;
        }

        if (command == "scrub") {
            await ScrubAsync(context, pages, path, flash);
            return;
        }

        if (command == "install") {
            await InstallSeedAsync(context, pages, path, flash);
            return;
        }

        if (command == "restore") {
            await RestoreRevisionAsync(context, pages, path, flash);
            return;
        }

        await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
            "editor.unknown_command",
            "The editor could not tell what to do",
            "That request did not name an editor action this page understands. Use one of the buttons on the editor page.",
            [new(PageUrls.Editor(path), "Back to editor")]));
    }

    private static async Task InstallSeedAsync(HttpContext context, PageEditor pages, PagePath path, Flash flash) {
        SecurityHeaders.NoStore(context);
        string? seed = await PageAssetSeeder.ReadSeedTemplateAsync(path, context.RequestAborted);
        if (seed is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        PageTemplate? live = await pages.GetAsync(path, context.RequestAborted);
        if (live is not null && string.Equals(live.Template, seed, StringComparison.Ordinal)) {
            await WriteNothingToDoAsync(
                context,
                "editor.install_nothing_to_do",
                "That page already holds its seed",
                $"Nothing was installed: {path.Key} already has the seed's source. Open the page to edit it, or its history to see how it got there.",
                path,
                PageUrls.EditorInstall(path));
            return;
        }

        SavedRevision saved = await pages.SaveAsync(path, seed, context.RequestAborted);
        context.RequestServices.GetService<ILogger<EditorEndpoint>>()
            ?.LogInformation("Installed the seed of {Path} as revision {Hash}", path.Key, saved.Revision.Hash);
        await flash.WriteAsync(
            context,
            ListingPath,
            InstallMessage(path, saved),
            context.RequestAborted);
        Redirect.SeeOther(context, "/edit");
    }

    // A restore is the author's deliberate act on the live page, so it appends whatever they chose.
    // The one thing it refuses is the choice that would change nothing: source the page already holds.
    private static async Task RestoreRevisionAsync(HttpContext context, PageEditor pages, PagePath path, Flash flash) {
        SecurityHeaders.NoStore(context);
        if (!TryGetRevisionHash(context, out string? hash)) {
            await WriteInvalidRestoreLinkAsync(context, path);
            return;
        }

        if (await pages.GetHeadAsync(path, context.RequestAborted) is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        PageRevisionTemplate? revision = await pages.GetRevisionTemplateByHashAsync(hash, context.RequestAborted);
        if (revision is null || revision.Path != path) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        PageTemplate? live = await pages.GetAsync(path, context.RequestAborted);
        if (live is not null && string.Equals(live.Template, revision.Template, StringComparison.Ordinal)) {
            await WriteNothingToDoAsync(
                context,
                "editor.restore_nothing_to_do",
                "There is nothing to restore",
                $"{ShortHash(revision.Hash)} has the source {path.Key} already holds, so nothing was written. Restore a different revision, or edit the page.",
                path,
                PageUrls.EditorRestore(path, revision.Hash));
            return;
        }

        SavedRevision saved = await pages.SaveAsync(path, revision.Template, context.RequestAborted);
        context.RequestServices.GetService<ILogger<EditorEndpoint>>()
            ?.LogInformation("Restored revision {Hash} of {Path} as revision {NewHash}", revision.Hash, path.Key, saved.Revision.Hash);
        await flash.WriteAsync(
            context,
            SessionPathFor(path),
            RestoreMessage(path, revision.Hash, saved),
            context.RequestAborted);
        Redirect.SeeOther(context, PageUrls.Editor(path));
    }

    // The refusal both actions share: the write would leave the live source exactly as it is, and a
    // row that says so is noise in a history whose job is to record changes.
    private static Task WriteNothingToDoAsync(
        HttpContext context,
        string code,
        string title,
        string detail,
        PagePath path,
        string confirmationUrl) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status409Conflict, new ErrorMessage(
            code,
            title,
            detail,
            [new(confirmationUrl, "Back to confirmation"), new(PageUrls.EditorCommand(path, "history"), "Back to history"), new(PageUrls.Editor(path), "Back to editor")]));

    private static Task WriteInvalidRestoreLinkAsync(HttpContext context, PagePath path) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
            "editor.restore_link",
            "That restore link is not valid",
            "A restore link needs a 64-character lowercase hexadecimal hash. Open the page history and choose a revision from the list.",
            [new(PageUrls.EditorCommand(path, "history"), "Back to history"), new(PageUrls.Editor(path), "Back to editor")]));

    private static string ShortHash(string hash) => hash[..Math.Min(12, hash.Length)];

    private static string InstallMessage(PagePath path, SavedRevision saved) =>
        $"{path.Key} was installed from its seed as revision {ShortHash(saved.Revision.Hash)}{OutcomeClause(OutcomeOf(saved.Diagnostics))}";

    private static string RestoreMessage(PagePath path, string restoredFrom, SavedRevision saved) =>
        $"Restored revision {ShortHash(restoredFrom)} of {path.Key} as revision {ShortHash(saved.Revision.Hash)}{OutcomeClause(OutcomeOf(saved.Diagnostics))}";

    // One classification of a compile's answer, so four verbs cannot describe it four ways.
    private static CompileOutcome OutcomeOf(IReadOnlyList<PageDiagnostic> diagnostics) => diagnostics switch {
        { Count: 0 } => CompileOutcome.Clean,
        _ when diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error) => CompileOutcome.Errors,
        _ => CompileOutcome.Warnings,
    };

    // The clause finishes a message that already names the revision, which is why the clean case is
    // a full stop rather than nothing.
    private static string OutcomeClause(CompileOutcome outcome) => outcome switch {
        CompileOutcome.Errors => ", which does not compile, so the page will not render until you fix it.",
        CompileOutcome.Warnings => ", which compiled with warnings.",
        _ => ".",
    };

    private static async Task ScrubAsync(HttpContext context, PageEditor pages, PagePath path, Flash flash) {
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

            await flash.WriteAsync(context, ListingPath, $"Scrubbed {path.Key} and its retained history.", context.RequestAborted);
            Redirect.SeeOther(context, "/edit");
        } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
            throw;
        } catch (Exception exception) {
            if (context.RequestServices is { } services) {
                services.GetService<ILogger<EditorEndpoint>>()?.LogError(exception, "Page scrub cleanup did not complete.");
            }
            await flash.WriteAsync(
                context,
                ListingPath,
                "Scrub cleanup did not complete. Check the deleted pages list; if the page remains there, scrub it again.",
                context.RequestAborted);
            await ShellDocument.BeginAsync(context, "Scrub incomplete",
            SiteNavigation.For(context), StatusCodes.Status503ServiceUnavailable);
            await context.Response.WriteAsync("<h1>Scrub cleanup did not complete</h1>");
            await context.Response.WriteAsync("<p>Check the deleted pages list; if the page remains there, scrub it again. It may already have been logically removed.</p>");
            await context.Response.WriteAsync(PageDocument.Nav("Recovery navigation", [new("/edit", "Page list")], "editor-menu"));
            await ShellDocument.EndAsync(context);
        }
    }

    // The session key is the canonical page key, not the escaped URL: the endpoint reads and writes
    // the same string, and it reads as the path it is.
    private static string SessionPathFor(PagePath path) => ListingPath + path.Key;

    // An authenticated identity without a name is anonymous as far as the session is concerned, so
    // a half-built principal fails closed rather than owning a session.
    private static PageUser? MapUser(HttpContext context) {
        if (context.User.Identity?.IsAuthenticated != true) {
            return null;
        }

        string? name = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrEmpty(name) ? null : new PageUser(name);
    }

    /// <summary>The editor's one-shot message, written under the path of the view that reads it.</summary>
    /// <remarks>
    /// A session value is addressed by page and key, so a message belongs to a landing path. Reading
    /// consumes it, which is what makes a confirmation show once rather than on every reload.
    /// </remarks>
    private sealed class Flash(
        PageSessionRequest session,
        IPageSessionStore store,
        PageSessionOptions options,
        PageSessionCookieOptions cookie) {

        public async Task WriteAsync(HttpContext context, string pagePath, string message, CancellationToken cancellationToken) {
            await session.SetTextAsync(store, options, pagePath, MessageKey, message, cancellationToken);

            // The session was minted by this write, so the browser has to learn its id before it
            // follows the redirect.
            if (session.NeedsCookie) {
                PageSessionCookie.Write(context, session.Id, cookie);
            }
        }

        public async Task ShowAsync(HttpContext context, string pagePath, CancellationToken cancellationToken) {
            string? message = await session.GetStringAsync(store, pagePath, MessageKey, cancellationToken);
            if (message is null) {
                return;
            }

            _ = await session.DeleteTextAsync(store, pagePath, MessageKey, cancellationToken);
            if (!string.IsNullOrEmpty(message)) {
                await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(message)}</p>");
            }
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
        "GET" => command is null or "history" or "revision" or "diff" or "delete" or "scrub" or "install" or "restore",
        "POST" => command is null or "save" or "save-and-view" or "new" or "delete" or "scrub" or "install" or "restore",
        _ => false,
    };

    private static bool TryGetRevisionHash(HttpContext context, out string hash) {
        return TryGetHash(context, "hash", out hash);
    }

    private static bool TryGetRevisionPair(HttpContext context, out string from, out string to) {
        from = string.Empty;
        to = string.Empty;

        return TryGetHash(context, "from", out from) && TryGetHash(context, "to", out to);
    }

    private static bool TryGetHash(HttpContext context, string name, out string hash) {
        hash = string.Empty;
        if (!context.Request.Query.TryGetValue(name, out StringValues values) || values.Count != 1) {
            return false;
        }

        // IsCanonical is the rule ?v= applies on the page endpoint, so a revision has one spelling
        // everywhere it can be named.
        string? candidate = values[0];
        if (!RevisionHash.IsCanonical(candidate)) {
            return false;
        }

        hash = candidate!;
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

        SavedRevision saved = await pages.SaveAsync(path, template.ToString(), context.RequestAborted);
        return OutcomeOf(saved.Diagnostics) switch {
            CompileOutcome.Clean => new SaveOutcome("Saved successfully.", Viewable: true),
            CompileOutcome.Errors => new SaveOutcome("Saved with errors.", Viewable: false),
            _ => new SaveOutcome("Saved with warnings.", Viewable: true),
        };
    }

    private readonly record struct SaveOutcome(string Message, bool Viewable);

    private enum CompileOutcome { Clean, Warnings, Errors }

    private static async Task WriteHistoryAsync(HttpContext context, PageEditor pages, PagePath path) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        IReadOnlyList<PageRevisionSummary> revisions = await pages.ListRevisionsAsync(path, context.RequestAborted);
        await ShellDocument.BeginAsync(context, "Page history",
            SiteNavigation.For(context), stylesheets: ["/editor.css"]);
        await context.Response.WriteAsync($"<h1>History for {WebUtility.HtmlEncode(path.Key)}</h1>");
        if (head.IsDeleted) {
            await context.Response.WriteAsync("<p>This page is deleted. Its revisions remain available here.</p>");
        }
        await context.Response.WriteAsync(PageDocument.Nav("Editor navigation", [
            new(PageUrls.Editor(path), "Back to editor"),
            new("/edit", "Page list"),
        ], "editor-menu"));
        if (revisions.Count == 0) {
            await context.Response.WriteAsync("<p>No saved revisions.</p>");
        } else {
            await context.Response.WriteAsync("<table><thead><tr><th scope=\"col\">Saved</th><th scope=\"col\">Revision</th><th scope=\"col\">State</th><th scope=\"col\">Compilation</th><th scope=\"col\">Creation runtime</th><th scope=\"col\">Action</th></tr></thead><tbody>");
            foreach (PageRevisionSummary revision in revisions) {
                string hash = WebUtility.HtmlEncode(revision.Hash);
                string url = WebUtility.HtmlEncode(PageUrls.EditorRevision(path, revision.Hash));
                string state = revision.IsCurrent ? "<strong>Current</strong>" : string.Empty;
                await context.Response.WriteAsync($"<tr><td>{WebUtility.HtmlEncode(revision.SavedAt.ToString("O", CultureInfo.InvariantCulture))}</td><td><a href=\"{url}\">{hash}</a></td><td>{state}</td><td>{WebUtility.HtmlEncode(revision.ImageState.Describe())}</td><td>{WebUtility.HtmlEncode(revision.CreationRuntimeId)}</td><td>{RestoreLink(path, revision)}</td></tr>");
            }
            await context.Response.WriteAsync("</tbody></table>");
            if (revisions.Count >= 2) {
                await WriteRevisionCompareFormAsync(context, revisions);
            } else {
                await context.Response.WriteAsync("<p>Only one revision is saved; there is nothing to compare yet.</p>");
            }
        }
        await ShellDocument.EndAsync(context);
    }

    // The current revision needs no link: restoring it would write the source the page already holds,
    // which both the confirmation and the POST refuse. That is a hint the listing can afford, because
    // it does not load revision source; an older row with the same text keeps its link and is refused
    // on the confirmation instead.
    private static string RestoreLink(PagePath path, PageRevisionSummary revision) {
        if (revision.IsCurrent) {
            return string.Empty;
        }

        string url = WebUtility.HtmlEncode(PageUrls.EditorRestore(path, revision.Hash));
        string label = WebUtility.HtmlEncode($"Restore revision {ShortHash(revision.Hash)} of {path.Key}");

        return $"<a href=\"{url}\" aria-label=\"{label}\">Restore</a>";
    }

    private static async Task WriteRevisionCompareFormAsync(
        HttpContext context,
        IReadOnlyList<PageRevisionSummary> revisions) {
        await context.Response.WriteAsync("""
            <form method="get" action="?" class="revision-compare">
                <input type="hidden" name="cmd" value="diff">
                <label for="diff-from">Compare</label>
                <select id="diff-from" name="from">
            """);
        await WriteRevisionOptionsAsync(context, revisions, revisions[1].Hash);
        await context.Response.WriteAsync("</select><label for=\"diff-to\">with</label><select id=\"diff-to\" name=\"to\">");
        await WriteRevisionOptionsAsync(context, revisions, revisions[0].Hash);
        await context.Response.WriteAsync("</select><button type=\"submit\">Compare revisions</button></form>");
    }

    private static async Task WriteRevisionOptionsAsync(
        HttpContext context,
        IReadOnlyList<PageRevisionSummary> revisions,
        string selectedHash) {
        foreach (PageRevisionSummary revision in revisions) {
            string hash = WebUtility.HtmlEncode(revision.Hash);
            string savedAt = WebUtility.HtmlEncode(revision.SavedAt.ToString("O", CultureInfo.InvariantCulture));
            string selected = string.Equals(revision.Hash, selectedHash, StringComparison.Ordinal) ? " selected" : string.Empty;
            string current = revision.IsCurrent ? " · Current" : string.Empty;
            await context.Response.WriteAsync(
                $"<option value=\"{hash}\"{selected}>{savedAt} · {hash[..12]}{current}</option>");
        }
    }

    private static async Task WriteDiffAsync(
        HttpContext context,
        PageEditor pages,
        DiffRenderer diff,
        PagePath path,
        string fromHash,
        string toHash) {
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        if (head is null) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        PageRevisionTemplate? from = await pages.GetRevisionTemplateByHashAsync(fromHash, context.RequestAborted);
        PageRevisionTemplate? to = await pages.GetRevisionTemplateByHashAsync(toHash, context.RequestAborted);
        if (from is null || from.Path != path || to is null || to.Path != path) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        await ShellDocument.BeginAsync(context, "Compare revisions",
            SiteNavigation.For(context), stylesheets: ["/editor.css"]);
        await context.Response.WriteAsync($"<h1>Changes to {WebUtility.HtmlEncode(path.Key)}</h1>");
        if (head.IsDeleted) {
            await context.Response.WriteAsync("<p>This page is deleted. Its revisions remain available here.</p>");
        }
        await context.Response.WriteAsync(PageDocument.Nav("Editor navigation", [
            new(PageUrls.EditorCommand(path, "history"), "Back to history"),
            new(PageUrls.Editor(path), "Back to editor"),
        ], "editor-menu"));

        string fromUrl = WebUtility.HtmlEncode(PageUrls.EditorRevision(path, from.Hash));
        string toUrl = WebUtility.HtmlEncode(PageUrls.EditorRevision(path, to.Hash));
        string fromSavedAt = WebUtility.HtmlEncode(from.SavedAt.ToString("O", CultureInfo.InvariantCulture));
        string toSavedAt = WebUtility.HtmlEncode(to.SavedAt.ToString("O", CultureInfo.InvariantCulture));
        await context.Response.WriteAsync($"""
            <p>From <a href="{fromUrl}">{WebUtility.HtmlEncode(from.Hash)}</a> saved <time datetime="{fromSavedAt}">{fromSavedAt}</time>, to <a href="{toUrl}">{WebUtility.HtmlEncode(to.Hash)}</a> saved <time datetime="{toSavedAt}">{toSavedAt}</time>.</p>
            """);

        await context.Response.WriteAsync(diff.ToHtml(from.Template, to.Template));
        await ShellDocument.EndAsync(context);
    }

    private static async Task WriteRevisionAsync(HttpContext context, PageEditor pages, PagePath path, string hash) {
        PageRevisionTemplate? revision = await pages.GetRevisionTemplateByHashAsync(hash, context.RequestAborted);
        if (revision is null || revision.Path != path) {
            await NotFound.WriteAsync(context, path);
            return;
        }

        await ShellDocument.BeginAsync(context, "Saved revision",
            SiteNavigation.For(context), stylesheets: ["/editor.css"]);
        await context.Response.WriteAsync($"<h1>Saved revision for {WebUtility.HtmlEncode(path.Key)}</h1>");
        await context.Response.WriteAsync($"<p>Saved {WebUtility.HtmlEncode(revision.SavedAt.ToString("O", CultureInfo.InvariantCulture))}; {WebUtility.HtmlEncode(revision.ImageState.Describe())}; creation runtime {WebUtility.HtmlEncode(revision.CreationRuntimeId)}.</p>");
        await context.Response.WriteAsync(PageDocument.Nav("Revision navigation", [
            new(PageUrls.EditorCommand(path, "history"), "Back to history"),
        ], "editor-menu"));
        await context.Response.WriteAsync($"<pre class=\"source\" tabindex=\"0\">{WebUtility.HtmlEncode(revision.Template)}</pre>");
        await ShellDocument.EndAsync(context);
    }
}
