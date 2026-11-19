// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.AspNetCore.Antiforgery;
using System.Globalization;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;
using static uk.osric.asp.Web.Pages.PageSeedInstaller;

/// <summary>The one shell page for the seed pack and for the rows a shell path keeps.</summary>
/// <remarks>
/// <para>
/// The seed section lists every page the release ships and writes exactly the rows the owner ticks.
/// A drifted row is offered rather than treated as a stop: the owner chose it, and an unticked row is
/// a deliberate omission. The four facts the preview was drawn from travel in the form, so a row that
/// changed in the meantime is refused and the rest of the run continues.
/// </para>
/// <para>
/// The second section finds every path the store holds a head, revision or state for that the shell
/// answers. No page at such a key can be served, so the section is the only way to remove one. It
/// proves every selected key is still reserved before it deletes, and the owner types the word to
/// confirm the irreversible batch.
/// </para>
/// <para>
/// The page draws <see cref="AdminPage"/>'s bare document, like the recovery page it replaces: it
/// must answer when a seeded stylesheet, script or layout is what is broken. Every refusal is a bare
/// document too, rather than the shell's error page.
/// </para>
/// </remarks>
internal static class SeedsEndpoint {

    /// <summary>The route both forms and the diff view answer on.</summary>
    public const string Path = "/admin/seeds";

    private const string ActionField = "action";
    private const string ApplyAction = "apply";
    private const string SeedField = "seed";
    private const string StrayField = "stray";
    private const string ConfirmField = "confirm";
    private const string ScrubAction = "scrub";
    private const string ConfirmWord = "scrub";

    /// <summary>Shows the seed table, the shell-path table, or one row's diff.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store read and written through the editor's saver.</param>
    /// <param name="seeds">The pack this release ships.</param>
    /// <param name="store">The store, for the rows at shell paths.</param>
    /// <param name="reserved">The keys the shell answers.</param>
    /// <param name="antiforgery">Mints the forms' tokens.</param>
    /// <param name="diff">Renders one seed's change.</param>
    public static async Task GetAsync(
        HttpContext context,
        PageEditor pages,
        PageSeeds seeds,
        IPageStore store,
        ReservedPaths reserved,
        IAntiforgery antiforgery,
        DiffRenderer diff) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reserved);
        ArgumentNullException.ThrowIfNull(antiforgery);
        ArgumentNullException.ThrowIfNull(diff);

        if (context.Request.Query["diff"].ToString() is { Length: > 0 } diffKey) {
            await WriteDiffAsync(context, pages, seeds, diff, diffKey);
            return;
        }

        IReadOnlyList<SeedPlan> plan = await PlanAsync(pages, seeds, context.RequestAborted);
        IReadOnlyList<StrayRow> strays = await StrayRowsAsync(store, reserved, context.RequestAborted);
        bool seedWork = plan.Any(seed => ActionOf(seed) != SeedAction.None);

        // Minted before the first byte: setting the cookie is a header write, and the tables are
        // written by the time a form's button is reached.
        AntiforgeryTokenSet? tokens = seedWork || strays.Count > 0
            ? antiforgery.GetAndStoreTokens(context)
            : null;

        await AdminPage.BeginAsync(context, "Seeds");
        await context.Response.WriteAsync("<h1>Seeds</h1>", context.RequestAborted);
        await context.Response.WriteAsync(AdminPage.BackLinks, context.RequestAborted);
        await WriteSeedSectionAsync(context, plan, tokens);
        await WriteStraySectionAsync(context, strays, tokens);
        await AdminPage.EndAsync(context);
    }

    /// <summary>Applies the ticked seed rows or scrubs the ticked shell-path rows.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="pages">The page store, through the saver every seed write uses.</param>
    /// <param name="seeds">The pack this release ships.</param>
    /// <param name="store">The store, for the rows at shell paths.</param>
    /// <param name="reserved">The keys the shell answers.</param>
    /// <param name="loggers">Names a failed write.</param>
    public static async Task PostAsync(
        HttpContext context,
        PageEditor pages,
        PageSeeds seeds,
        IPageStore store,
        ReservedPaths reserved,
        ILoggerFactory loggers) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reserved);
        ArgumentNullException.ThrowIfNull(loggers);

        if (context.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }) {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "seeds.form_expired",
                "The seed form expired",
                "The request could not be verified, so nothing was written. Reload the page and try again.");
            return;
        }

        if (!context.Request.HasFormContentType) {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "seeds.no_form",
                "That request carried no form",
                "Nothing was written. Reload the page and use one of its forms.");
            return;
        }

        IFormCollection form;
        try {
            form = await context.Request.ReadFormAsync(context.RequestAborted);
        } catch (InvalidDataException) {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "seeds.bad_form",
                "The form could not be read",
                "Nothing was written. Reload the page and try again.");
            return;
        }

        string action = Present(form, ActionField) ?? string.Empty;
        if (string.Equals(action, ApplyAction, StringComparison.Ordinal)) {
            await ApplyAsync(context, form, pages, seeds, loggers);
        } else if (string.Equals(action, ScrubAction, StringComparison.Ordinal)) {
            await ScrubAsync(context, form, store, reserved, loggers);
        } else {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "seeds.unknown_action",
                "That form was not one this page offered",
                "Nothing was written. Reload the page and try again.");
        }
    }

    private static async Task WriteSeedSectionAsync(
        HttpContext context,
        IReadOnlyList<SeedPlan> plan,
        AntiforgeryTokenSet? tokens) {
        bool work = plan.Any(seed => ActionOf(seed) != SeedAction.None);

        await context.Response.WriteAsync("""
            <h2 id="seed-updates">Apply seed updates</h2>
            <p>Every page this release ships, and what the store holds for it. Tick a row to write it;
            a row you leave unticked is left exactly as it is. Writing a seed compiles it first and
            saves it as a new revision, so the source it replaces stays in the page's history.</p>
            <p>The rows that could replace your own source &mdash; edited, conflict, deleted and unknown
            &mdash; start unticked. A tick is your instruction to replace what is there.</p>
            <p>If a page changes after this page loads, its tick is refused and nothing is written for
            that row; the others still run.</p>
            """, context.RequestAborted);

        if (work && tokens is not null) {
            await context.Response.WriteAsync($"""
                <form method="post" action="{Path}">
                    <input type="hidden" name="{ActionField}" value="{ApplyAction}">
                    {AntiforgeryField.Hidden(tokens)}
                """, context.RequestAborted);
        }

        await context.Response.WriteAsync("""
            <table>
                <caption>Every page this release ships</caption>
                <thead><tr><th scope="col">Page</th><th scope="col">Verdict</th><th scope="col">What it is</th><th scope="col">Apply</th></tr></thead>
                <tbody>
            """, context.RequestAborted);

        for (int index = 0; index < plan.Count; index++) {
            SeedPlan seed = plan[index];
            SeedAction action = ActionOf(seed);
            string editor = WebUtility.HtmlEncode(seed.Path.Editor(EditorPaths.Of(context.RequestServices)));

            await context.Response.WriteAsync($"""
                <tr>
                    <th scope="row"><a href="{editor}">{WebUtility.HtmlEncode(seed.Path.Key)}</a></th>
                    <td>{WebUtility.HtmlEncode(seed.Summary.Verdict)}</td>
                    <td>{WebUtility.HtmlEncode(seed.Summary.Sentence)}</td>
                    <td>{ApplyCell(seed, index, action, tokens)}</td>
                </tr>
                """, context.RequestAborted);
        }

        await context.Response.WriteAsync("</tbody></table>", context.RequestAborted);

        if (work && tokens is not null) {
            await context.Response.WriteAsync("""
                    <p><button type="submit">Apply the ticked seeds</button></p>
                </form>
                """, context.RequestAborted);
        } else {
            await context.Response.WriteAsync(
                "<p>Every seed this release moved already matches the store, so there is nothing to apply.</p>",
                context.RequestAborted);
        }
    }

    private static string ApplyCell(
        SeedPlan seed,
        int index,
        SeedAction action,
        AntiforgeryTokenSet? tokens) {
        if (action == SeedAction.None || tokens is null) {
            return "&mdash;";
        }

        string number = index.ToString(CultureInfo.InvariantCulture);
        string id = "seed-" + number;
        string label = TickLabel(seed, action);
        string diff = action == SeedAction.Write && !seed.HoldsSeed
            ? $" <a href=\"{WebUtility.HtmlEncode(Path + "?diff=" + PageUrls.Page(seed.Path))}\">See what would change</a>"
            : string.Empty;
        string hidden = string.Concat(
            Hidden(WasName(number, "key"), seed.Path.Key),
            Hidden(WasName(number, "deleted"), seed.Head?.IsDeleted == true ? "1" : "0"),
            Hidden(WasName(number, "anchor"), seed.Head?.SeedHash ?? string.Empty),
            Hidden(WasName(number, "content"), seed.Head?.ContentHash ?? string.Empty),
            Hidden(WasName(number, "seed"), seed.SeedHash));
        string ticked = seed.Summary.CanApply || seed.Summary.CanAdopt ? " checked" : string.Empty;

        return $"{hidden}<input type=\"checkbox\" id=\"{id}\" name=\"{SeedField}\" value=\"{number}\"{ticked}><label for=\"{id}\">{WebUtility.HtmlEncode(label)}</label>{diff}";
    }

    private static async Task WriteStraySectionAsync(
        HttpContext context,
        IReadOnlyList<StrayRow> strays,
        AntiforgeryTokenSet? tokens) {
        await context.Response.WriteAsync("""
            <h2 id="shell-paths">Stored pages at shell paths</h2>
            <p>A key the shell answers serves the shell's own file, not a stored page. A row here is
            unreachable: it is what is left when a seed became a shell file, or when an import or an
            older build wrote a key the shell had taken. Ticking a row and typing <code>scrub</code>
            removes its head, every revision and its page state for good.</p>
            <p>The page proves every key is still a shell path before it removes anything; a normal
            page can never be scrubbed from here. Export the archive first if you want the bytes.</p>
            """, context.RequestAborted);

        if (strays.Count == 0) {
            await context.Response.WriteAsync(
                "<p>No stored page is at a key the shell answers.</p>", context.RequestAborted);
            return;
        }

        if (tokens is not null) {
            await context.Response.WriteAsync($"""
                <form method="post" action="{Path}">
                    <input type="hidden" name="{ActionField}" value="{ScrubAction}">
                    {AntiforgeryField.Hidden(tokens)}
                """, context.RequestAborted);
        }

        await context.Response.WriteAsync("""
            <table>
                <caption>Keys the shell answers with a stored page</caption>
                <thead><tr><th scope="col">Key</th><th scope="col">Head</th><th scope="col">Revisions</th><th scope="col">Scrub</th></tr></thead>
                <tbody>
            """, context.RequestAborted);

        for (int index = 0; index < strays.Count; index++) {
            StrayRow stray = strays[index];
            string id = "stray-" + index.ToString(CultureInfo.InvariantCulture);
            string head = stray.Head is null ? "no head" : stray.Head.IsDeleted ? "tombstone" : "live";

            await context.Response.WriteAsync($"""
                <tr>
                    <th scope="row">{WebUtility.HtmlEncode(stray.Path.Key)}</th>
                    <td>{head}</td>
                    <td>{stray.Revisions.ToString(CultureInfo.InvariantCulture)}</td>
                    <td><input type="checkbox" id="{id}" name="{StrayField}" value="{WebUtility.HtmlEncode(stray.Path.Key)}"><label for="{id}">Remove this key and its history</label></td>
                </tr>
                """, context.RequestAborted);
        }

        await context.Response.WriteAsync("""
                </tbody>
            </table>
            <p><label for="scrub-confirm">Type scrub to confirm</label>
            <input id="scrub-confirm" name="confirm" type="text" autocomplete="off" required></p>
            <p><button type="submit">Scrub the ticked rows</button></p>
            """, context.RequestAborted);

        if (tokens is not null) {
            await context.Response.WriteAsync("</form>", context.RequestAborted);
        }
    }

    private static async Task WriteDiffAsync(
        HttpContext context,
        PageEditor pages,
        PageSeeds seeds,
        DiffRenderer diff,
        string key) {
        PagePath path = PagePath.Normalize(key);
        SeedFile? seed = seeds.Find(path);
        if (seed is null) {
            await RefuseAsync(context, StatusCodes.Status404NotFound, "seeds.not_a_seed",
                "That is not a seeded page",
                $"{path.Key} is not a page this release ships, so there is no seed to compare with.");
            return;
        }

        PageTemplate? live = await pages.GetAsync(path, context.RequestAborted);
        PageHeadSummary? head = await pages.GetHeadAsync(path, context.RequestAborted);
        PageSeedSummary summary = head is null
            ? SeedVerdictWords.New()
            : head.IsDeleted
                ? SeedVerdictWords.Deleted()
                : SeedComparison.Of(head.SeedHash, seed.Hash, head.ContentHash).Describe(seed.Hash);

        await AdminPage.BeginAsync(context, "What would change");
        await context.Response.WriteAsync(
            $"<h1>What writing the seed of {WebUtility.HtmlEncode(path.Key)} would change</h1>",
            context.RequestAborted);
        await context.Response.WriteAsync(
            $"<p><strong>{WebUtility.HtmlEncode(summary.Verdict)}</strong>: {WebUtility.HtmlEncode(summary.Sentence)}</p>",
            context.RequestAborted);
        await context.Response.WriteAsync(diff.ToHtml(live?.Template ?? string.Empty, seed.Source), context.RequestAborted);
        await context.Response.WriteAsync(AdminPage.BackLinks, context.RequestAborted);
        await context.Response.WriteAsync($"<p><a href=\"{Path}\">Back to seeds</a></p>", context.RequestAborted);
        await AdminPage.EndAsync(context);
    }

    private static async Task ApplyAsync(
        HttpContext context,
        IFormCollection form,
        PageEditor pages,
        PageSeeds seeds,
        ILoggerFactory loggers) {
        IReadOnlyList<SeedPlan> plan = await PlanAsync(pages, seeds, context.RequestAborted);

        List<int> selected = [];
        foreach (string? value in form[SeedField]) {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || index < 0
                || index >= plan.Count) {
                await RefuseAsync(context, StatusCodes.Status400BadRequest, "seeds.bad_selection",
                    "That form was not one this page offered",
                    "Nothing was written. Reload the page and tick the rows again.");
                return;
            }

            selected.Add(index);
        }

        if (selected.Count == 0) {
            List<SeedResult> none = [.. plan.Select(seed => new SeedResult(seed.Path, seed.Summary, SeedOutcome.NotTicked))];
            await WriteApplyResultAsync(context, none, "Nothing was ticked, so nothing was written.");
            return;
        }

        HashSet<int> chosen = [.. selected];
        HashSet<int> stale = [];
        foreach (int index in chosen) {
            WasState? was = ReadWas(form, index);
            if (was is null || !string.Equals(was.Key, plan[index].Path.Key, StringComparison.Ordinal)) {
                await RefuseAsync(context, StatusCodes.Status400BadRequest, "seeds.stale_form",
                    "The release changed while this form was open",
                    "Nothing was written. Reload the page and choose the rows again.");
                return;
            }

            if (was != StateOf(plan[index])) {
                stale.Add(index);
            }
        }

        List<SeedResult> results = [];
        ILogger logger = loggers.CreateLogger(typeof(SeedsEndpoint).FullName!);

        for (int index = 0; index < plan.Count; index++) {
            SeedPlan seed = plan[index];
            if (!chosen.Contains(index)) {
                results.Add(new SeedResult(seed.Path, seed.Summary, SeedOutcome.NotTicked));
                continue;
            }

            if (stale.Contains(index)) {
                results.Add(new SeedResult(seed.Path, seed.Summary, SeedOutcome.Stale));
                continue;
            }

            switch (ActionOf(seed)) {
                case SeedAction.None:
                    results.Add(new SeedResult(seed.Path, seed.Summary, SeedOutcome.Unchanged));
                    break;
                case SeedAction.Adopt:
                    bool adopted = await pages.AdoptAsync(seed.Path, seed.SeedHash, context.RequestAborted);
                    results.Add(new SeedResult(seed.Path, seed.Summary, adopted ? SeedOutcome.Adopted : SeedOutcome.Stale));
                    if (adopted) {
                        logger.LogInformation("Recorded {Path} as installed from its seed", seed.Path.Key);
                    } else {
                        logger.LogWarning("Could not record {Path} as installed; it changed while the page was open", seed.Path.Key);
                    }
                    break;
                case SeedAction.Write:
                    try {
                        SeedResult result = await InstallAsync(pages, seed, context.RequestAborted);
                        results.Add(result);
                        if (result.Outcome == SeedOutcome.WrittenWithErrors) {
                            logger.LogWarning("Wrote the seed of {Path} as revision {Hash} with compiler errors", seed.Path.Key, result.Hash);
                        } else {
                            logger.LogInformation("Wrote the seed of {Path} as revision {Hash}", seed.Path.Key, result.Hash);
                        }
                    } catch (Exception exception) when (exception is not OperationCanceledException) {
                        logger.LogError(exception, "Writing the seed of {Path} failed", seed.Path.Key);
                        results.Add(new SeedResult(seed.Path, seed.Summary, SeedOutcome.Failed));
                    }
                    break;
            }
        }

        await WriteApplyResultAsync(context, results, note: null);
    }

    private static async Task ScrubAsync(
        HttpContext context,
        IFormCollection form,
        IPageStore store,
        ReservedPaths reserved,
        ILoggerFactory loggers) {
        List<string> selected = [.. form[StrayField]
            .Select(value => value ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))];

        if (selected.Count == 0) {
            await WriteScrubResultAsync(context, [], "Nothing was ticked, so nothing was removed.");
            return;
        }

        if (!string.Equals(Present(form, ConfirmField), ConfirmWord, StringComparison.Ordinal)) {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "seeds.scrub_unconfirmed",
                "The scrub was not confirmed",
                "Type scrub to confirm the removal. Nothing was removed.");
            return;
        }

        IReadOnlyList<PagePath> strays = await ReservedPageScan.FindAsync(store, reserved, context.RequestAborted);
        HashSet<string> present = new(strays.Select(path => path.Key), StringComparer.Ordinal);
        List<PagePath> targets = [];
        foreach (string value in selected) {
            PagePath path = PagePath.Normalize(value);
            if (!reserved.IsReserved(path)) {
                await RefuseAsync(context, StatusCodes.Status400BadRequest, "seeds.not_a_shell_path",
                    "That is not a shell path",
                    "Nothing was removed: the form named a key the shell does not answer.");
                return;
            }

            targets.Add(path);
        }

        ILogger logger = loggers.CreateLogger(typeof(SeedsEndpoint).FullName!);
        List<ScrubResult> results = [];
        foreach (PagePath path in targets.DistinctBy(path => path.Key)) {
            if (!present.Contains(path.Key)) {
                results.Add(new ScrubResult(path, null, "already gone"));
                continue;
            }

            try {
                PageScrubResult removed = await store.ScrubStoredPathAsync(path, context.RequestAborted);
                results.Add(new ScrubResult(path, removed, removed.Any ? "removed" : "already gone"));
                logger.LogInformation(
                    "Scrubbed {Path}: {Head} head(s), {Revisions} revision(s), {State} state key(s)",
                    path.Key, removed.Head ? 1 : 0, removed.Revisions, removed.StateKeys);
            } catch (Exception exception) when (exception is not OperationCanceledException) {
                logger.LogError(exception, "Scrubbing {Path} failed", path.Key);
                results.Add(new ScrubResult(path, null, "failed"));
            }
        }

        await WriteScrubResultAsync(context, results, note: null);
    }

    private static async Task<IReadOnlyList<StrayRow>> StrayRowsAsync(
        IPageStore store,
        ReservedPaths reserved,
        CancellationToken cancellationToken) {
        IReadOnlyList<PagePath> paths = await ReservedPageScan.FindAsync(store, reserved, cancellationToken);
        List<StrayRow> rows = [];
        foreach (PagePath path in paths) {
            PageHeadSummary? head = await store.GetHeadAsync(path, cancellationToken);
            IReadOnlyList<PageRevisionSummary> revisions = await store.ListRevisionsAsync(path, cancellationToken);
            rows.Add(new StrayRow(path, head, revisions.Count));
        }

        return rows;
    }

    private static async Task WriteApplyResultAsync(
        HttpContext context,
        IReadOnlyList<SeedResult> results,
        string? note) {
        int written = results.Count(result => result.Outcome is SeedOutcome.Written or SeedOutcome.WrittenWithErrors);
        int adopted = results.Count(result => result.Outcome == SeedOutcome.Adopted);
        int left = results.Count(result => result.Outcome == SeedOutcome.NotTicked);
        int stale = results.Count(result => result.Outcome == SeedOutcome.Stale);
        int failed = results.Count(result => result.Outcome == SeedOutcome.Failed);
        string title = note is null ? "Applied the ticked seeds" : "Nothing was written";

        await AdminPage.BeginAsync(context, title);
        await context.Response.WriteAsync($"<h1>{title}</h1>", context.RequestAborted);
        await context.Response.WriteAsync(AdminPage.BackLinks, context.RequestAborted);
        if (note is not null) {
            await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(note)}</p>", context.RequestAborted);
        }

        await context.Response.WriteAsync($"""
            <p>{written.ToString(CultureInfo.InvariantCulture)} written,
            {adopted.ToString(CultureInfo.InvariantCulture)} recorded as installed,
            {left.ToString(CultureInfo.InvariantCulture)} left unticked,
            {stale.ToString(CultureInfo.InvariantCulture)} changed since the page loaded,
            {failed.ToString(CultureInfo.InvariantCulture)} failed.</p>
            """, context.RequestAborted);

        if (failed > 0) {
            await context.Response.WriteAsync(
                "<p>The pages that failed are unchanged; the log names the error. The rest of the run is in the table.</p>",
                context.RequestAborted);
        }

        await context.Response.WriteAsync("""
            <table>
                <caption>What this run did</caption>
                <thead><tr><th scope="col">Page</th><th scope="col">Verdict now</th><th scope="col">Outcome</th></tr></thead>
                <tbody>
            """, context.RequestAborted);

        foreach (SeedResult result in results) {
            string editor = WebUtility.HtmlEncode(result.Path.Editor(EditorPaths.Of(context.RequestServices)));
            await context.Response.WriteAsync($"""
                <tr>
                    <th scope="row"><a href="{editor}">{WebUtility.HtmlEncode(result.Path.Key)}</a></th>
                    <td>{WebUtility.HtmlEncode(result.Was.Verdict)}</td>
                    <td>{Describe(result.Outcome)}</td>
                </tr>
                """, context.RequestAborted);
        }

        await context.Response.WriteAsync("</tbody></table>", context.RequestAborted);
        await AdminPage.EndAsync(context);
    }

    private static async Task WriteScrubResultAsync(
        HttpContext context,
        IReadOnlyList<ScrubResult> results,
        string? note) {
        int removed = results.Count(result => string.Equals(result.Outcome, "removed", StringComparison.Ordinal));
        int gone = results.Count(result => string.Equals(result.Outcome, "already gone", StringComparison.Ordinal));
        int failed = results.Count(result => string.Equals(result.Outcome, "failed", StringComparison.Ordinal));
        string title = note is null ? "Scrubbed the ticked shell paths" : "Nothing was removed";

        await AdminPage.BeginAsync(context, title);
        await context.Response.WriteAsync($"<h1>{title}</h1>", context.RequestAborted);
        await context.Response.WriteAsync(AdminPage.BackLinks, context.RequestAborted);
        if (note is not null) {
            await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(note)}</p>", context.RequestAborted);
        }

        await context.Response.WriteAsync($"""
            <p>{removed.ToString(CultureInfo.InvariantCulture)} removed,
            {gone.ToString(CultureInfo.InvariantCulture)} already gone,
            {failed.ToString(CultureInfo.InvariantCulture)} failed.</p>
            """, context.RequestAborted);

        if (failed > 0) {
            await context.Response.WriteAsync(
                "<p>The rows that failed are unchanged; the log names the error. The rest of the run is in the table.</p>",
                context.RequestAborted);
        }

        await context.Response.WriteAsync("""
            <table>
                <caption>What this scrub did</caption>
                <thead><tr><th scope="col">Key</th><th scope="col">Outcome</th><th scope="col">Removed</th></tr></thead>
                <tbody>
            """, context.RequestAborted);

        foreach (ScrubResult result in results) {
            string detail = result.Removed is null
                ? "&mdash;"
                : $"{(result.Removed.Head ? "head, " : string.Empty)}{result.Removed.Revisions.ToString(CultureInfo.InvariantCulture)} revision(s), {result.Removed.StateKeys.ToString(CultureInfo.InvariantCulture)} state key(s)";
            await context.Response.WriteAsync($"""
                <tr>
                    <th scope="row">{WebUtility.HtmlEncode(result.Path.Key)}</th>
                    <td>{WebUtility.HtmlEncode(result.Outcome)}</td>
                    <td>{detail}</td>
                </tr>
                """, context.RequestAborted);
        }

        await context.Response.WriteAsync("</tbody></table>", context.RequestAborted);
        await AdminPage.EndAsync(context);
    }

    private static async Task RefuseAsync(
        HttpContext context,
        int status,
        string code,
        string title,
        string summary) {
        context.Response.StatusCode = status;
        await AdminPage.BeginAsync(context, title);
        await context.Response.WriteAsync($"<h1>{WebUtility.HtmlEncode(title)}</h1>", context.RequestAborted);
        await context.Response.WriteAsync($"<p>{WebUtility.HtmlEncode(summary)}</p>", context.RequestAborted);
        await context.Response.WriteAsync(AdminPage.BackLinks, context.RequestAborted);
        await context.Response.WriteAsync($"<p><a href=\"{Path}\">Back to the seed page</a></p>", context.RequestAborted);
        await context.Response.WriteAsync(
            $"<p class=\"error-code\">Error code: <code>{WebUtility.HtmlEncode(code)}</code></p>",
            context.RequestAborted);
        await AdminPage.EndAsync(context);
    }

    private static SeedAction ActionOf(SeedPlan seed) =>
        seed.Summary.CanAdopt
            ? SeedAction.Adopt
            : seed.HoldsSeed
                ? SeedAction.None
                : SeedAction.Write;

    private static string TickLabel(SeedPlan seed, SeedAction action) {
        if (action == SeedAction.Adopt) {
            return "Record it as installed";
        }

        return seed.Summary.Verdict switch {
            "new" => "Create the page",
            "deleted" => "Bring the page back",
            "release newer" => "Write the newer seed",
            _ => "Replace it with the bundled seed",
        };
    }

    private static string Describe(SeedOutcome outcome) => outcome switch {
        SeedOutcome.Unchanged => "already matched",
        SeedOutcome.Written => "written",
        SeedOutcome.WrittenWithErrors => "written with errors",
        SeedOutcome.Adopted => "recorded as installed",
        SeedOutcome.NotTicked => "not ticked",
        SeedOutcome.Stale => "changed since the page loaded; nothing written",
        SeedOutcome.Failed => "failed",
        _ => "unknown",
    };

    private static string WasName(string number, string fact) =>
        $"was.{number}.{fact}";

    private static string Hidden(string name, string value) =>
        $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(name)}\" value=\"{WebUtility.HtmlEncode(value)}\">";

    private static WasState? ReadWas(IFormCollection form, int index) {
        string number = index.ToString(CultureInfo.InvariantCulture);
        string? key = Present(form, WasName(number, "key"));
        string? deleted = Present(form, WasName(number, "deleted"));
        string? anchor = Present(form, WasName(number, "anchor"));
        string? content = Present(form, WasName(number, "content"));
        string? seed = Present(form, WasName(number, "seed"));

        return key is null || deleted is null || anchor is null || content is null || seed is null
            ? null
            : new WasState(key, deleted, anchor, content, seed);
    }

    private static WasState StateOf(SeedPlan seed) => new(
        seed.Path.Key,
        seed.Head?.IsDeleted == true ? "1" : "0",
        seed.Head?.SeedHash ?? string.Empty,
        seed.Head?.ContentHash ?? string.Empty,
        seed.SeedHash);

    private static string? Present(IFormCollection form, string name) =>
        form.ContainsKey(name) ? form[name].ToString() : null;

    private enum SeedAction {
        None,
        Write,
        Adopt,
    }

    private sealed record WasState(string Key, string Deleted, string Anchor, string Content, string Seed);

    private sealed record StrayRow(PagePath Path, PageHeadSummary? Head, int Revisions);

    private sealed record ScrubResult(PagePath Path, PageScrubResult? Removed, string Outcome);
}
