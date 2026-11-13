// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;
using System.Net;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;
using static uk.osric.asp.Web.Pages.PageSeedInstaller;

/// <summary>The emergency restore: every seed back to its bundled source in one POST.</summary>
/// <remarks>
/// <para>
/// This is the way back when a stored page that the site itself needs — the editor, its sheet, its
/// module or the layout — will not load. It is not a reset and not an import: it only writes the
/// pages the release ships, a seed that already matches is not written, and a page the owner made is
/// never touched. Every write goes through <see cref="PageEditor.SaveAsync"/>, so it compiles before
/// it stores exactly as any other save does.
/// </para>
/// <para>
/// The document is written here rather than through <see cref="Html.ShellDocument"/>, and it carries
/// no stylesheet and no script. The shell's document always links <c>/site.css</c>, which is a seeded
/// page the owner can break — a single <c>* { display: none; }</c> would hide the one page that can
/// put it back — so the recovery page depends on nothing it is there to recover.
/// </para>
/// </remarks>
internal static class RestoreEndpoint {

    /// <summary>Shows every seed and what restoring it would change.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store, through the saver every write uses.</param>
    /// <param name="antiforgery">Mints the form's token.</param>
    public static async Task PreviewAsync(HttpContext context, PageEditor pages, IAntiforgery antiforgery) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pages);

        IReadOnlyList<SeedPlan> plan = await PlanAsync(pages, context.RequestAborted);
        int work = plan.Count(seed => seed.State != SeedState.Current);

        // Minted before the first byte. Storing the antiforgery cookie is a header write, and the
        // table has gone out by the time the button is written, so a visitor who arrives without one
        // - the cookie is a session cookie and the sign-in cookie is not - would lose the button.
        AntiforgeryTokenSet? tokens = work == 0 ? null : antiforgery.GetAndStoreTokens(context);

        await BeginAsync(context, "Restore every seed");
        await context.Response.WriteAsync("<h1>Restore every seed</h1>");
        await context.Response.WriteAsync(BackLinks);
        await context.Response.WriteAsync("""
            <p>This page carries no stylesheet and no script, so it still works when a seeded sheet,
            script or layout has been broken.</p>
            <p>This writes the bundled source of every page this release ships with. It is the way
            back when the editor, its sheet, its module or the layout will not load.</p>
            <p>A seed that already matches is not written, a page you made is never touched, and a
            restored page keeps its history: each one is saved as a new revision.</p>
            """);

        await WriteStateTableAsync(context, plan);

        if (tokens is null) {
            await context.Response.WriteAsync("<p>Every seed already carries its bundled source, so there is nothing to restore.</p>");
        } else {
            await context.Response.WriteAsync($"""
                <form method="post" action="/admin/restore" class="form">
                    {AntiforgeryField.Hidden(tokens)}
                    <button type="submit">{ButtonText(work)}</button>
                </form>
                """);
        }

        await EndAsync(context);
    }

    /// <summary>Restores every seed that differs, reporting each one rather than stopping at a failure.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store, through the saver every write uses.</param>
    /// <param name="loggers">Names a seed that failed.</param>
    public static async Task RestoreAsync(HttpContext context, PageEditor pages, ILoggerFactory loggers) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(loggers);

        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "restore.form_expired",
                "The restore form expired",
                "The request could not be verified, so nothing was restored. Reload the page and press the button again.",
                [new("/admin/restore", "Back to the restore page")]));
            return;
        }

        ILogger logger = loggers.CreateLogger(typeof(RestoreEndpoint).FullName!);
        IReadOnlyList<SeedPlan> plan = await PlanAsync(pages, context.RequestAborted);
        List<SeedResult> results = [];

        foreach (SeedPlan seed in plan) {
            if (seed.State == SeedState.Current) {
                results.Add(new SeedResult(seed.Path, seed.State, SeedOutcome.Unchanged));
                continue;
            }

            try {
                SeedResult result = await InstallAsync(pages, seed, context.RequestAborted);
                results.Add(result);

                if (result.Outcome == SeedOutcome.SavedWithErrors) {
                    logger.LogWarning("Restored the seed of {Path} as revision {Hash} with compiler errors", seed.Path.Key, result.Hash);
                } else {
                    logger.LogInformation("Restored the seed of {Path} as revision {Hash}", seed.Path.Key, result.Hash);
                }
            } catch (Exception exception) when (exception is not OperationCanceledException) {
                logger.LogError(exception, "Restoring the seed of {Path} failed", seed.Path.Key);
                results.Add(new SeedResult(seed.Path, seed.State, SeedOutcome.Failed));
            }
        }

        await WriteResultAsync(context, results);
    }

    private static async Task WriteStateTableAsync(HttpContext context, IReadOnlyList<SeedPlan> plan) {
        await context.Response.WriteAsync("""
            <table>
                <caption>Every page this release ships with</caption>
                <thead>
                    <tr><th scope="col">Page</th><th scope="col">State</th></tr>
                </thead>
                <tbody>
            """);

        foreach (SeedPlan seed in plan) {
            string key = WebUtility.HtmlEncode(seed.Path.Key);
            await context.Response.WriteAsync(
                $"<tr><th scope=\"row\"><a href=\"{WebUtility.HtmlEncode(PageUrls.Editor(seed.Path, EditorPaths.Of(context.RequestServices)))}\">{key}</a></th><td>{Describe(seed.State)}</td></tr>");
        }

        await context.Response.WriteAsync("</tbody></table>");
    }

    private static async Task WriteResultAsync(HttpContext context, IReadOnlyList<SeedResult> results) {
        int restored = results.Count(result => result.Outcome is SeedOutcome.Restored or SeedOutcome.SavedWithErrors);
        int failed = results.Count(result => result.Outcome == SeedOutcome.Failed);
        int unchanged = results.Count(result => result.Outcome == SeedOutcome.Unchanged);

        await BeginAsync(context, "Restored the seeds");
        await context.Response.WriteAsync("<h1>Restored the seeds</h1>");
        await context.Response.WriteAsync(BackLinks);
        await context.Response.WriteAsync(
            $"<p>{restored.ToString(CultureInfo.InvariantCulture)} restored, {failed.ToString(CultureInfo.InvariantCulture)} failed, {unchanged.ToString(CultureInfo.InvariantCulture)} already matched.</p>");

        if (failed > 0) {
            await context.Response.WriteAsync("<p>The pages that failed are unchanged; the log names the error. The rest were restored.</p>");
        }

        await context.Response.WriteAsync("""
            <table>
                <caption>What this run did</caption>
                <thead>
                    <tr><th scope="col">Page</th><th scope="col">Was</th><th scope="col">Outcome</th></tr>
                </thead>
                <tbody>
            """);

        foreach (SeedResult result in results) {
            string key = WebUtility.HtmlEncode(result.Path.Key);
            await context.Response.WriteAsync(
                $"<tr><th scope=\"row\"><a href=\"{WebUtility.HtmlEncode(PageUrls.Editor(result.Path, EditorPaths.Of(context.RequestServices)))}\">{key}</a></th><td>{Describe(result.Was)}</td><td>{Describe(result.Outcome)}</td></tr>");
        }

        await context.Response.WriteAsync("</tbody></table>");
        await EndAsync(context);
    }

    private const string BackLinks =
        """<p><a href="/admin">Administration</a> · <a href="/status">Status</a></p>""";

    private static async Task BeginAsync(HttpContext context, string title) {
        context.Response.ContentType = "text/html; charset=utf-8";
        SecurityHeaders.NoStoreIfAbsent(context);

        await context.Response.WriteAsync($"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(title)}</title>
            </head>
            <body>
            <a href="#main">Skip to content</a>
            <main id="main">
            """, context.RequestAborted);
    }

    private static Task EndAsync(HttpContext context) => context.Response.WriteAsync("""
        </main>
        </body>
        </html>
        """, context.RequestAborted);

    private static string ButtonText(int work) => work == 1
        ? "Restore the seed"
        : $"Restore {work.ToString(CultureInfo.InvariantCulture)} seeds";

    private static string Describe(SeedState state) => state switch {
        SeedState.Current => "current",
        SeedState.Changed => "changed",
        SeedState.Missing => "missing",
        SeedState.Deleted => "deleted",
        _ => "unknown",
    };

    private static string Describe(SeedOutcome outcome) => outcome switch {
        SeedOutcome.Unchanged => "unchanged",
        SeedOutcome.Restored => "restored",
        SeedOutcome.SavedWithErrors => "saved with errors",
        SeedOutcome.Failed => "failed",
        _ => "unknown",
    };

    private static bool HasErrors(IReadOnlyList<PageDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);
}
