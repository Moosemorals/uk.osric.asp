// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using System.Globalization;
using System.Net;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;
// The page contract has a diagnostic value of its own, so this file names the compiler's.
using PageDiagnosticSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;

/// <summary>The import views: upload and validate, review and approve, progress and results.</summary>
/// <remarks>
/// Nothing is written to storage until an approved batch commits in one transaction, so a rejected
/// archive and an abandoned review leave the site exactly as it was.
/// </remarks>
internal sealed class ImportEndpoint {

    /// <summary>How much room the multipart envelope needs above the archive itself.</summary>
    private const long FormOverheadBytes = 64 * 1024;

    /// <summary>The file part every upload must carry.</summary>
    private const string ArchiveField = "archive";

    /// <summary>The checkbox every selection uses.</summary>
    private const string IncludeField = "include";

    /// <summary>Renders the upload form.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="antiforgery">Mints the form's token.</param>
    /// <param name="options">The archive options, for the size the form describes.</param>
    public static async Task UploadFormAsync(HttpContext context, IAntiforgery antiforgery, ArchiveOptions options) {
        ArgumentNullException.ThrowIfNull(context);

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        SecurityHeaders.AllowScripts(context);

        await ShellDocument.BeginAsync(context, "Import pages",
            SiteNavigation.For(context), stylesheets: ["/admin.css"]);
        await context.Response.WriteAsync("<h1>Import pages</h1>");
        await context.Response.WriteAsync(AdminNavigation.Nav("import", EditorPaths.Of(context.RequestServices)));
        await context.Response.WriteAsync($"""
            <fieldset>
                <legend>Page archive</legend>
                <p>A page archive is a <code>.sqlite</code> file made by this site's export. It carries each page's source and the state stored with it, and no revision history.</p>
                <p>An archive is checked in full before anything is offered for import, and up to {DescribeSize(options.MaxUploadBytes)} is accepted.</p>
            </fieldset>
            <form method="post" action="/admin/import" enctype="multipart/form-data" class="form">
                {AntiforgeryField.Hidden(tokens)}
                <dl>
                    <dt><label for="archive">Archive file</label></dt>
                    <dd>
                        <div class="dropzone" id="dropzone">
                            <input type="file" id="archive" name="archive" accept=".sqlite,application/vnd.sqlite3,application/x-sqlite3" required>
                            <p class="drop-hint" id="drop-hint">Drop a .sqlite archive here, or choose a file.</p>
                        </div>
                    </dd>
                </dl>
                <button type="submit">Upload archive</button>
            </form>
            <script type="module" src="{WebUtility.HtmlEncode(await ShellDocument.AssetUrlAsync(context, "/admin.js", context.RequestAborted))}"></script>
            """);
        await ShellDocument.EndAsync(context);
    }

    /// <summary>Validates an uploaded archive and stages it, or explains why it was refused.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="reader">The archive reader.</param>
    /// <param name="stages">Where a validated archive is staged.</param>
    /// <param name="options">The archive options.</param>
    /// <param name="loggers">Names a refused upload in the journal.</param>
    public static async Task UploadAsync(
        HttpContext context,
        PageArchiveReader reader,
        ImportStageStore stages,
        ArchiveOptions options,
        ILoggerFactory loggers) {
        ArgumentNullException.ThrowIfNull(loggers);

        ILogger logger = loggers.CreateLogger(typeof(ImportEndpoint).FullName!);

        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "import.form_expired",
                "The upload form expired",
                "The form could not be verified, so nothing was staged. Reload the import page and choose the file again.",
                ImportRemedies(context)));
            return;
        }

        // Refuse rather than buffer: the limit is set before anything is read.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) {
            feature.MaxRequestBodySize = options.MaxUploadBytes + FormOverheadBytes;
        }

        IFormCollection form;
        try {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        } catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge) {
            // The size cap did its job: the sender is told, and the reader is the only one who would
            // want the parser's own words.
            logger.LogDebug(exception, "An uploaded archive was larger than {MaxUploadBytes} bytes and was refused", options.MaxUploadBytes);
            await WriteTooLargeAsync(context, options);
            return;
        } catch (InvalidDataException exception) {
            logger.LogDebug(exception, "An uploaded archive form could not be read");
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "import.bad_form",
                "That upload could not be read",
                "The form was malformed or larger than this site accepts, so nothing was staged. Choose a file and try again.",
                ImportRemedies(context)));
            return;
        }

        List<IFormFile> files = [.. form.Files.Where(file => string.Equals(file.Name, ArchiveField, StringComparison.Ordinal))];
        if (files.Count != 1) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "import.no_file",
                "That upload needs exactly one archive",
                "The upload carried no archive file, or more than one, so nothing was staged. Choose a single .sqlite file and try again.",
                ImportRemedies(context)));
            return;
        }

        byte[]? archive = await ReadBoundedAsync(context, files[0], options.MaxUploadBytes);
        if (archive is null) {
            await WriteTooLargeAsync(context, options);
            return;
        }

        PageArchiveReadResult result = reader.Read(archive);
        if (!result.Succeeded) {
            await WriteRejectedAsync(context, result.Problems);
            return;
        }

        ImportStage stage = stages.Create(result.Pages);
        Redirect.SeeOther(context, ReviewUrl(stage.Id));
    }

    /// <summary>Renders the staged archive for review.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="stages">The staged archives.</param>
    /// <param name="pages">The page store, for what the site already holds.</param>
    /// <param name="antiforgery">Mints the form's token.</param>
    public static async Task ReviewAsync(HttpContext context, ImportStageStore stages, IPageStore pages, IAntiforgery antiforgery) {
        ArgumentNullException.ThrowIfNull(context);

        ImportStage? stage = stages.Get(RouteId(context));
        if (stage is null) {
            await WriteExpiredAsync(context);
            return;
        }

        Dictionary<string, PageListing> heads = [];
        foreach (PageListing head in await pages.ListPagesAsync(context.RequestAborted)) {
            heads[head.Path.Key] = head;
        }

        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        SecurityHeaders.AllowScripts(context);

        await ShellDocument.BeginAsync(context, "Review import",
            SiteNavigation.For(context), stylesheets: ["/admin.css"]);
        await context.Response.WriteAsync($"<h1>Import {DescribeCount(stage.Pages.Count, "page")}</h1>");
        await context.Response.WriteAsync(AdminNavigation.Nav("import", EditorPaths.Of(context.RequestServices)));
        await context.Response.WriteAsync("""
            <p>The selected pages are applied in one step. If that step fails, none of them change.</p>
            """);
        await context.Response.WriteAsync($"""
            <form method="post" action="{WebUtility.HtmlEncode(ReviewUrl(stage.Id))}">
                {AntiforgeryField.Hidden(tokens)}
                <p id="import-count" data-verb="Import">{DescribeCount(stage.Pages.Count, "page")} selected.</p>
                <ul class="admin-pages" role="list">
            """);

        for (int index = 0; index < stage.Pages.Count; index++) {
            PageArchiveEntry entry = stage.Pages[index];
            string id = "include-" + index.ToString(CultureInfo.InvariantCulture);
            string key = WebUtility.HtmlEncode(entry.Path.Key);
            string tag = Tag(heads.GetValueOrDefault(entry.Path.Key));
            string size = DescribeSize(entry.SourceBytes);
            string state = entry.StateKeys > 0
                ? $", {DescribeCount(entry.StateKeys, "state key")}"
                : string.Empty;
            await context.Response.WriteAsync($"""
                <li><input type="checkbox" id="{id}" name="{IncludeField}" value="{key}" checked><label for="{id}">{key}</label> <span class="tag">{tag}</span> <span class="size">{size}{state}</span></li>
                """);
        }

        await context.Response.WriteAsync($"""
                </ul>
                <p class="admin-actions">
                    <button type="submit">Import {DescribeCount(stage.Pages.Count, "selected page")}</button>
                    <a href="/admin/import">Cancel</a>
                </p>
            </form>
            <script type="module" src="{WebUtility.HtmlEncode(await ShellDocument.AssetUrlAsync(context, "/admin.js", context.RequestAborted))}"></script>
            """);
        await ShellDocument.EndAsync(context);
    }

    /// <summary>Approves the ticked pages and hands the batch to the worker.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="stages">The staged archives.</param>
    /// <param name="queue">The import queue.</param>
    /// <param name="reserved">The keys the site's own routes answer for.</param>
    /// <param name="loggers">Names a refused approval in the journal.</param>
    public static async Task ApproveAsync(
        HttpContext context,
        ImportStageStore stages,
        ImportQueue queue,
        ReservedPaths reserved,
        ILoggerFactory loggers) {
        ArgumentNullException.ThrowIfNull(loggers);

        ImportStage? stage = stages.Get(RouteId(context));
        if (stage is null) {
            await WriteExpiredAsync(context);
            return;
        }

        if (stage.IsRunning) {
            await WriteAlreadyRunningAsync(context, stage.Id);
            return;
        }

        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            await WriteBadSelectionAsync(context, stage.Id, "The form could not be verified.");
            return;
        }

        if (!context.Request.HasFormContentType) {
            await WriteBadSelectionAsync(context, stage.Id, "The approval carried no form.");
            return;
        }

        IFormCollection form;
        try {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        } catch (InvalidDataException exception) {
            loggers.CreateLogger(typeof(ImportEndpoint).FullName!)
                .LogDebug(exception, "The approval form for import {StageId} could not be read", stage.Id);
            await WriteBadSelectionAsync(context, stage.Id, "The approval form could not be read.");
            return;
        }

        HashSet<string> offered = new(stage.Pages.Select(page => page.Path.Key), StringComparer.Ordinal);
        List<PagePath> selected = [];
        foreach (string? value in form[IncludeField]) {
            if (value is null || !offered.Contains(value)) {
                // The review offered a fixed set; a value from outside it is a stale or crafted form.
                await WriteBadSelectionAsync(context, stage.Id, "The approval named a page the review did not offer.");
                return;
            }

            PagePath path = PagePath.Normalize(value);
            if (!path.IsAddressable || reserved.IsReserved(path)) {
                await WriteBadSelectionAsync(context, stage.Id, $"{MessageText.Sanitize(path.Key)} cannot be imported.");
                return;
            }

            selected.Add(path);
        }

        if (selected.Count == 0) {
            await WriteBadSelectionAsync(context, stage.Id, "Nothing was ticked, so nothing was imported.");
            return;
        }

        if (!queue.TryReserve()) {
            await WriteQueueFullAsync(context, stage.Id);
            return;
        }

        if (!stage.TryApprove([.. selected.OrderBy(path => path.Key, StringComparer.Ordinal)])) {
            queue.ReleaseReservation();
            await WriteAlreadyRunningAsync(context, stage.Id);
            return;
        }

        if (!queue.TryEnqueueReserved(stage.Id)) {
            queue.ReleaseReservation();
            stage.Unqueue();
            await WriteQueueFullAsync(context, stage.Id);
            return;
        }

        Redirect.SeeOther(context, ProgressUrl(stage.Id));
    }

    /// <summary>Renders the batch's progress and results, refreshing while it runs.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="stages">The staged archives.</param>
    public static async Task ProgressAsync(HttpContext context, ImportStageStore stages) {
        ArgumentNullException.ThrowIfNull(context);

        ImportStage? stage = stages.Get(RouteId(context));
        if (stage is null) {
            await WriteExpiredAsync(context);
            return;
        }

        ImportStageSnapshot snapshot = stage.Snapshot();
        bool running = snapshot.Status is ImportBatchStatus.Queued or ImportBatchStatus.Compiling or ImportBatchStatus.Saving;

        await ShellDocument.BeginAsync(
            context,
            "Import progress",
            SiteNavigation.For(context),
            stylesheets: ["/admin.css"],
            head: running ? """<meta http-equiv="refresh" content="2">""" : null);
        await context.Response.WriteAsync("<h1>Import progress</h1>");
        await context.Response.WriteAsync(AdminNavigation.Nav("import", EditorPaths.Of(context.RequestServices)));
        await context.Response.WriteAsync($"<p id=\"import-status\">{WebUtility.HtmlEncode(Summary(snapshot))}</p>");

        if (snapshot.Results.Count > 0) {
            await context.Response.WriteAsync("<ul class=\"admin-pages\" role=\"list\">");
            foreach (ImportPageResult result in snapshot.Results) {
                string key = WebUtility.HtmlEncode(result.Path.Key);
                string editor = WebUtility.HtmlEncode(PageUrls.Editor(result.Path, EditorPaths.Of(context.RequestServices)));
                await context.Response.WriteAsync($"""
                    <li><a href="{editor}">{key}</a> <span class="tag">{WebUtility.HtmlEncode(Describe(result.Status))}</span></li>
                    """);

                if (result.Diagnostics.Count > 0) {
                    await context.Response.WriteAsync(PageLayout.Diagnostics(DiagnosticLine.From(result.Diagnostics)));
                }
            }
            await context.Response.WriteAsync("</ul>");
        }

        if (snapshot.Refused.Count > 0) {
            await context.Response.WriteAsync("<p>The pages that would not fit:</p><ul>");
            foreach (PagePath path in snapshot.Refused) {
                await context.Response.WriteAsync($"<li>{WebUtility.HtmlEncode(path.Key)}</li>");
            }
            await context.Response.WriteAsync("</ul>");
        }

        if (running) {
            await context.Response.WriteAsync($"""<p><a href="{WebUtility.HtmlEncode(ProgressUrl(stage.Id))}">Refresh now</a></p>""");
        } else {
            await context.Response.WriteAsync($"""
                <p>
                    <a href="{WebUtility.HtmlEncode(EditorPaths.Of(context.RequestServices))}">Page list</a>
                    <a href="{WebUtility.HtmlEncode(ReviewUrl(stage.Id))}">Back to the review</a>
                    <a href="/admin/export">Export pages</a>
                    <a href="/admin/import">Import another archive</a>
                </p>
                """);
        }

        await ShellDocument.EndAsync(context);
    }

    private static string Summary(ImportStageSnapshot snapshot) => snapshot.Status switch {
        ImportBatchStatus.Reviewing => "This archive has not been approved for import.",
        ImportBatchStatus.Queued => "Waiting to start…",
        ImportBatchStatus.Compiling => $"Compiling {snapshot.Results.Count(result => result.Status != ImportPageStatus.Waiting).ToString(CultureInfo.InvariantCulture)} of {DescribeCount(snapshot.Results.Count, "page")}…",
        ImportBatchStatus.Saving => $"Saving {DescribeCount(snapshot.Selected.Count, "page")}…",
        ImportBatchStatus.Committed => snapshot.Message ?? "Every selected page was saved.",
        _ => snapshot.Message ?? "The import failed, so none of the selected pages changed.",
    };

    private static string Describe(ImportPageStatus status) => status switch {
        ImportPageStatus.StoredAsSource => "stored as source",
        ImportPageStatus.Compiled => "compiled",
        ImportPageStatus.CompiledWithWarnings => "compiled with warnings",
        ImportPageStatus.CompiledWithErrors => "compiled with errors",
        ImportPageStatus.CompilerFault => "compiled with a compiler fault",
        ImportPageStatus.Library => "checked as a library",
        ImportPageStatus.LibraryWithIssues => "checked as a library, with issues",
        _ => "not compiled",
    };

    private static string Tag(PageListing? head) => head switch {
        null => "new",
        { IsDeleted: true } => "restore",
        _ => "overwrite",
    };

    private static string DescribeCount(int count, string noun) {
        string text = count.ToString(CultureInfo.InvariantCulture);
        return count == 1 ? $"{text} {noun}" : $"{text} {noun}s";
    }

    private static string DescribeSize(long bytes) {
        if (bytes >= 1024) {
            double kibibytes = bytes / 1024.0;
            return string.Create(CultureInfo.InvariantCulture, $"{kibibytes:0.#} KiB");
        }

        return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
    }

    // The copy counts bytes as it goes rather than trusting Content-Length or the declared length.
    private static async Task<byte[]?> ReadBoundedAsync(HttpContext context, IFormFile file, int maxBytes) {
        await using Stream source = file.OpenReadStream();
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        long total = 0;

        int read;
        while ((read = await source.ReadAsync(chunk, context.RequestAborted)) > 0) {
            total += read;
            if (total > maxBytes) {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string? RouteId(HttpContext context) => context.Request.RouteValues["id"] as string;

    private static string ReviewUrl(string id) => "/admin/import/" + Uri.EscapeDataString(id);

    private static string ProgressUrl(string id) => ReviewUrl(id) + "/progress";

    private static IReadOnlyList<NavLink> ImportRemedies(HttpContext context) =>
        [new("/admin/import", "Import pages"), new("/admin/export", "Export pages"), new(EditorPaths.Of(context.RequestServices), "Page list")];

    private static IReadOnlyList<NavLink> ReviewRemedies(string id, HttpContext context) =>
        [new(ReviewUrl(id), "Back to the review"), new("/admin/import", "Import pages"), new(EditorPaths.Of(context.RequestServices), "Page list")];

    private static Task WriteExpiredAsync(HttpContext context) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound, new ErrorMessage(
            "import.expired",
            "That import is no longer available",
            "A staged archive is kept in memory only and expires, so this one is gone. Upload the archive again to review it.",
            [new("/admin/import", "Upload the archive again"), new(EditorPaths.Of(context.RequestServices), "Page list")]),
            context.RequestAborted);

    private static Task WriteTooLargeAsync(HttpContext context, ArchiveOptions options) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status413PayloadTooLarge, new ErrorMessage(
            "import.too_large",
            "That archive is too large",
            $"An archive may be at most {DescribeSize(options.MaxUploadBytes)}. Nothing was staged.",
            ImportRemedies(context)),
            context.RequestAborted);

    private static Task WriteBadSelectionAsync(HttpContext context, string id, string summary) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
            "import.bad_selection",
            "That approval could not be used",
            summary + " Nothing was imported.",
            ReviewRemedies(id, context)),
            context.RequestAborted);

    private static Task WriteAlreadyRunningAsync(HttpContext context, string id) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status409Conflict, new ErrorMessage(
            "import.running",
            "That import is already running",
            "This archive has already been approved and is being imported. Its progress page shows how it is getting on.",
            [new(ProgressUrl(id), "Import progress"), new(EditorPaths.Of(context.RequestServices), "Page list")]),
            context.RequestAborted);

    private static Task WriteQueueFullAsync(HttpContext context, string id) =>
        ErrorResponses.WriteAsync(context, StatusCodes.Status409Conflict, new ErrorMessage(
            "import.queue_full",
            "Too many imports are waiting",
            "This site runs one import at a time and the queue is full. Wait for the running import to finish, then try again.",
            [new(ReviewUrl(id), "Back to the review"), new(EditorPaths.Of(context.RequestServices), "Page list")]),
            context.RequestAborted);

    private static async Task WriteRejectedAsync(HttpContext context, IReadOnlyList<string> problems) {
        await ShellDocument.BeginAsync(context, "Import failed",
            SiteNavigation.For(context), StatusCodes.Status400BadRequest, stylesheets: ["/admin.css"]);
        await context.Response.WriteAsync("<h1>That archive could not be imported</h1>");
        await context.Response.WriteAsync(AdminNavigation.Nav("import", EditorPaths.Of(context.RequestServices)));
        await context.Response.WriteAsync(PageLayout.Diagnostics(
            [.. problems.Select(problem => new DiagnosticLine(problem, PageDiagnosticSeverity.Error, null))]));
        await context.Response.WriteAsync("<p>Nothing was staged or saved.</p>");
        await context.Response.WriteAsync(ShellDocument.Nav("Recovery navigation", [
            new("/admin/import", "Choose another file"),
            new(EditorPaths.Of(context.RequestServices), "Page list"),
        ], "editor-menu"));
        await ShellDocument.EndAsync(context);
    }
}
