// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;
using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;
using static uk.osric.asp.Web.Pages.PageSeedInstaller;

/// <summary>The routine seed apply: what a release moved, written on the gate and nothing else.</summary>
/// <remarks>
/// <para>
/// It walks the pack in ordinal key order. It writes every <em>release newer</em> row, creates every
/// <em>new</em> one and records every row whose content already is the shipped seed, and it skips the
/// rows that are not its business: <em>in sync</em>, <em>deleted</em> and <em>retired</em>. The first
/// row that has drifted — <em>edited</em>, <em>conflict</em>, or <em>unknown</em> with content that is
/// not the seed — stops the run, and the rows after it are not written.
/// </para>
/// <para>
/// Its counterpart is <see cref="RestoreEndpoint"/>: that one is the way back when the site is broken,
/// writes everything it ships and reports every outcome. This one is the routine apply, and it stops
/// rather than leaving a half-applied site the operator believes is applied.
/// </para>
/// </remarks>
internal static class ApplySeedsEndpoint {

    /// <summary>Shows what an apply would write, and what would stop it.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store, through the saver every write uses.</param>
    /// <param name="seeds">The pack this release ships.</param>
    /// <param name="antiforgery">Mints the form's token.</param>
    public static async Task PreviewAsync(HttpContext context, PageEditor pages, PageSeeds seeds, IAntiforgery antiforgery) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seeds);

        IReadOnlyList<SeedPlan> plan = await PlanAsync(pages, seeds, context.RequestAborted);
        SeedPlan? stop = plan.FirstOrDefault(seed => Stops(seed.Summary));
        int work = plan.Count(seed => seed.Summary.CanApply || seed.Summary.CanAdopt);

        AntiforgeryTokenSet? tokens = work == 0 ? null : antiforgery.GetAndStoreTokens(context);

        await AdminPage.BeginAsync(context, "Apply seed updates");
        await context.Response.WriteAsync("<h1>Apply seed updates</h1>");
        await context.Response.WriteAsync(AdminPage.BackLinks);
        await context.Response.WriteAsync("""
            <p>This writes what the release moved and leaves everything else alone. A page the
            release did not move, a page you changed, and a page you deleted are not written.</p>
            """);

        await WriteTableAsync(context, plan);

        if (stop is not null) {
            await context.Response.WriteAsync($"""
                <p><strong>The run would stop at {WebUtility.HtmlEncode(stop.Path.Key)}</strong>, which is
                <em>{WebUtility.HtmlEncode(stop.Summary.Verdict)}</em>: {WebUtility.HtmlEncode(stop.Summary.Sentence)}
                Nothing after it is written.</p>
                """);
        }

        if (tokens is null) {
            await context.Response.WriteAsync("<p>Nothing needs applying: every seed the release moved is already in the store.</p>");
        } else {
            await context.Response.WriteAsync($"""
                <form method="post" action="/admin/seed-apply" class="form">
                    {AntiforgeryField.Hidden(tokens)}
                    <button type="submit">{ButtonText(work)}</button>
                </form>
                """);
        }

        await AdminPage.EndAsync(context);
    }

    /// <summary>Writes the rows the gate allows, and stops at the first that has drifted.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store, through the saver every write uses.</param>
    /// <param name="seeds">The pack this release ships.</param>
    /// <param name="loggers">Names a seed that failed.</param>
    public static async Task ApplyAsync(HttpContext context, PageEditor pages, PageSeeds seeds, ILoggerFactory loggers) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(loggers);

        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            await ErrorResponses.WriteAsync(context, StatusCodes.Status400BadRequest, new ErrorMessage(
                "apply.form_expired",
                "The apply form expired",
                "The request could not be verified, so nothing was applied. Reload the page and press the button again.",
                [new("/admin/seed-apply", "Back to the apply page")]));
            return;
        }

        ILogger logger = loggers.CreateLogger(typeof(ApplySeedsEndpoint).FullName!);
        IReadOnlyList<SeedPlan> plan = await PlanAsync(pages, seeds, context.RequestAborted);
        List<SeedResult> results = [];
        bool stopped = false;

        foreach (SeedPlan seed in plan) {
            if (Stops(seed.Summary)) {
                results.Add(new SeedResult(seed.Path, seed.Summary, SeedOutcome.Stopped));
                stopped = true;
                break;
            }

            if (seed.Summary.CanAdopt) {
                bool adopted = await pages.AdoptAsync(seed.Path, seed.SeedHash, context.RequestAborted);
                results.Add(new SeedResult(seed.Path, seed.Summary, adopted ? SeedOutcome.Adopted : SeedOutcome.Failed));
                continue;
            }

            if (!seed.Summary.CanApply) {
                results.Add(new SeedResult(seed.Path, seed.Summary, SeedOutcome.Unchanged));
                continue;
            }

            try {
                SeedResult result = await InstallAsync(pages, seed, context.RequestAborted);
                results.Add(result);
                logger.LogInformation("Applied the seed of {Path} as revision {Hash}", seed.Path.Key, result.Hash);
            } catch (Exception exception) when (exception is not OperationCanceledException) {
                logger.LogError(exception, "Applying the seed of {Path} failed", seed.Path.Key);
                results.Add(new SeedResult(seed.Path, seed.Summary, SeedOutcome.Failed));
            }
        }

        await WriteResultAsync(context, results, stopped);
    }

    // A row this run is responsible for stopping on: it has drifted, and the gate may not write it.
    private static bool Stops(PageSeedSummary summary) =>
        !summary.CanApply && !summary.CanAdopt && summary.Verdict is "edited" or "conflict" or "unknown";

    private static async Task WriteTableAsync(HttpContext context, IReadOnlyList<SeedPlan> plan) {
        await context.Response.WriteAsync("""
            <table>
                <caption>Every page this release ships with</caption>
                <thead>
                    <tr><th scope="col">Page</th><th scope="col">Verdict</th><th scope="col">What an apply does</th></tr>
                </thead>
                <tbody>
            """);

        foreach (SeedPlan seed in plan) {
            string key = WebUtility.HtmlEncode(seed.Path.Key);
            string action = seed.Summary.CanApply
                ? "writes the seed"
                : seed.Summary.CanAdopt
                    ? "records the seed as installed"
                    : Stops(seed.Summary)
                        ? "stops here"
                        : "leaves alone";
            await context.Response.WriteAsync(
                $"<tr><th scope=\"row\"><a href=\"{WebUtility.HtmlEncode(seed.Path.Editor(EditorPaths.Of(context.RequestServices)))}\">{key}</a></th><td>{WebUtility.HtmlEncode(seed.Summary.Verdict)}</td><td>{action}</td></tr>");
        }

        await context.Response.WriteAsync("</tbody></table>");
    }

    private static async Task WriteResultAsync(HttpContext context, IReadOnlyList<SeedResult> results, bool stopped) {
        int written = results.Count(result => result.Outcome is SeedOutcome.Restored or SeedOutcome.SavedWithErrors);
        int adopted = results.Count(result => result.Outcome == SeedOutcome.Adopted);
        int failed = results.Count(result => result.Outcome == SeedOutcome.Failed);
        int unchanged = results.Count(result => result.Outcome == SeedOutcome.Unchanged);

        await AdminPage.BeginAsync(context, "Applied the seed updates");
        await context.Response.WriteAsync("<h1>Applied the seed updates</h1>");
        await context.Response.WriteAsync(AdminPage.BackLinks);
        await context.Response.WriteAsync(
            $"<p>{written.ToString(CultureInfo.InvariantCulture)} written, {adopted.ToString(CultureInfo.InvariantCulture)} recorded as installed, {unchanged.ToString(CultureInfo.InvariantCulture)} already matched, {failed.ToString(CultureInfo.InvariantCulture)} failed.</p>");

        if (stopped) {
            await context.Response.WriteAsync("<p>The run stopped at a page that has drifted; the pages after it were not written.</p>");
        }

        if (failed > 0) {
            await context.Response.WriteAsync("<p>The pages that failed are unchanged; the log names the error.</p>");
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
                $"<tr><th scope=\"row\"><a href=\"{WebUtility.HtmlEncode(result.Path.Editor(EditorPaths.Of(context.RequestServices)))}\">{key}</a></th><td>{WebUtility.HtmlEncode(result.Was.Verdict)}</td><td>{Describe(result.Outcome)}</td></tr>");
        }

        await context.Response.WriteAsync("</tbody></table>");
        await AdminPage.EndAsync(context);
    }

    private static string ButtonText(int work) => work == 1
        ? "Apply one seed"
        : $"Apply {work.ToString(CultureInfo.InvariantCulture)} seeds";

    private static string Describe(SeedOutcome outcome) => outcome switch {
        SeedOutcome.Unchanged => "already matched",
        SeedOutcome.Restored => "written",
        SeedOutcome.Adopted => "recorded as installed",
        SeedOutcome.SavedWithErrors => "written with errors",
        SeedOutcome.Failed => "failed",
        SeedOutcome.Stopped => "stopped here",
        _ => "unknown",
    };

}
