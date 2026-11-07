// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>The export view: a checkbox list of live pages and a streaming download.</summary>
/// <remarks>
/// The list offers every live page that is addressable and not reserved — the same test the editor
/// uses to call a page reachable — because a page on a reserved key could never be imported back,
/// and a deleted page has no current source to export.
/// </remarks>
internal sealed class ExportEndpoint {

    /// <summary>Renders the list of exportable pages.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store.</param>
    /// <param name="reserved">The keys the site's own routes answer for.</param>
    /// <param name="antiforgery">Mints the form's token.</param>
    public static async Task ListAsync(HttpContext context, IPageStore pages, ReservedPaths reserved, IAntiforgery antiforgery) {
        IReadOnlyList<PageHeadSummary> heads = await pages.ListPagesAsync(context.RequestAborted);
        PageHeadSummary[] reachable = [.. heads
            .Where(head => !head.IsDeleted && head.Path.IsAddressable && !reserved.IsReserved(head.Path))
            .OrderBy(head => head.Path.Key, StringComparer.Ordinal)];

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        SecurityHeaders.AllowScripts(context);

        await ShellDocument.BeginAsync(context, "Export pages", stylesheet: "/admin.css");
        await context.Response.WriteAsync("<h1>Export pages</h1>");
        await AdminNavigationAsync(context);
        await context.Response.WriteAsync("""
            <p>Choose the pages to put in the archive. Each one carries its source and the state it has stored.</p>
            """);

        if (reachable.Length == 0) {
            await context.Response.WriteAsync("<p>No pages can be exported.</p>");
        } else {
            await context.Response.WriteAsync($"""
                <form method="post" action="/admin/export">
                    {AntiforgeryField.Hidden(tokens)}
                    <p id="export-count" data-verb="Export">{reachable.Length.ToString(CultureInfo.InvariantCulture)} pages selected.</p>
                    <p class="admin-actions">
                        <button type="button" id="select-all">All</button>
                        <button type="button" id="select-none">None</button>
                    </p>
                    <ul class="admin-pages" role="list">
                """);

            for (int index = 0; index < reachable.Length; index++) {
                string id = "include-" + index.ToString(CultureInfo.InvariantCulture);
                string key = WebUtility.HtmlEncode(reachable[index].Path.Key);
                await context.Response.WriteAsync($"""<li><input type="checkbox" id="{id}" name="include" value="{key}" checked><label for="{id}">{key}</label></li>""");
            }

            await context.Response.WriteAsync("""
                    </ul>
                    <button type="submit">Export selected pages</button>
                </form>
                <script src="/admin.js" defer></script>
                """);
        }

        await ShellDocument.EndAsync(context);
    }

    /// <summary>Builds the archive from the ticked pages and writes it as a download.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store.</param>
    /// <param name="state">The state store.</param>
    /// <param name="reserved">The keys the site's own routes answer for.</param>
    public static async Task DownloadAsync(HttpContext context, IPageStore pages, IPageStateStore state, ReservedPaths reserved) {
        // The framework records a rejected token rather than short-circuiting, and reading a form it
        // rejected throws; this is the same check the editor makes before it touches a form.
        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            await WriteErrorAsync(context, new ErrorMessage(
                "export.form_expired",
                "The export form expired",
                "The form could not be verified, so no archive was made. Reload the export page and try again.",
                AdminRemedies(context)));
            return;
        }

        if (!context.Request.HasFormContentType) {
            await WriteErrorAsync(context, new ErrorMessage(
                "export.no_form",
                "That is not how an export is asked for",
                "An export posts the page list's form. Open the export page and tick the pages you want.",
                AdminRemedies(context)));
            return;
        }

        IFormCollection form;
        try {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        } catch (InvalidDataException) {
            await WriteErrorAsync(context, new ErrorMessage(
                "export.bad_form",
                "That form could not be read",
                "The uploaded form was malformed, so no archive was made. Open the export page and try again.",
                AdminRemedies(context)));
            return;
        }

        IReadOnlyList<PageHeadSummary> heads = await pages.ListPagesAsync(context.RequestAborted);
        Dictionary<string, PagePath> reachable = new(StringComparer.Ordinal);
        foreach (PageHeadSummary head in heads) {
            if (!head.IsDeleted && head.Path.IsAddressable && !reserved.IsReserved(head.Path)) {
                reachable[head.Path.Key] = head.Path;
            }
        }

        List<PagePath> selected = [];
        foreach (string? value in form["include"]) {
            // A stale form cannot ask for a page that has since been scrubbed, reserved or deleted.
            if (value is null || !reachable.TryGetValue(value, out PagePath? path)) {
                await WriteErrorAsync(context, new ErrorMessage(
                    "export.not_a_page",
                    "A selected page is no longer available",
                    $"{value} is not a page this site can export now. Open the export page and choose again.",
                    AdminRemedies(context)));
                return;
            }

            selected.Add(path);
        }

        if (selected.Count == 0) {
            await WriteErrorAsync(context, new ErrorMessage(
                "export.no_pages",
                "No pages were selected",
                "Nothing was ticked, so there was no archive to build. Open the export page and choose at least one page.",
                AdminRemedies(context)));
            return;
        }

        // Everything is read before the response starts, so a storage failure is a readable error
        // page rather than half a file.
        List<PageArchiveEntry> entries = [];
        foreach (PagePath path in selected.OrderBy(path => path.Key, StringComparer.Ordinal)) {
            string? source = await pages.GetSourceAsync(path, context.RequestAborted);
            if (source is null) {
                await WriteErrorAsync(context, new ErrorMessage(
                    "export.not_a_page",
                    "A selected page is no longer available",
                    $"{path.Key} changed while the archive was built. Open the export page and choose again.",
                    AdminRemedies(context)));
                return;
            }

            IReadOnlyList<PageStateValue> values = await state.GetValuesAsync(path, context.RequestAborted);
            entries.Add(new PageArchiveEntry(path, source, values));
        }

        byte[] archive = PageArchiveWriter.Write(entries);
        // A colon is not legal in a Windows file name, so the timestamp has none.
        string name = $"pages-export-{DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}.sqlite";

        context.Response.ContentType = "application/vnd.sqlite3";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{name}\"";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentLength = archive.Length;

        await context.Response.Body.WriteAsync(archive, context.RequestAborted);
    }

    private static Task AdminNavigationAsync(HttpContext context) =>
        context.Response.WriteAsync(AdminNavigation.Nav("export"));

    private static IReadOnlyList<NavLink> AdminRemedies(HttpContext context) => context.User.Identity?.IsAuthenticated == true
        ? [new("/admin/export", "Export pages"), new("/edit", "Page list")]
        : ErrorMessage.DefaultRemedies(isAuthenticated: false);

    private static Task WriteErrorAsync(HttpContext context, ErrorMessage message) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, message, context.RequestAborted);
}
