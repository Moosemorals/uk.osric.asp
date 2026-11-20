// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using CoreDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;
using CoreKind = uk.osric.asp.Core.Pages.PageKind;
using CoreSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;
using SdkDiagnostic = uk.osric.asp.Sdk.PageDiagnostic;
using SdkKind = uk.osric.asp.Sdk.PageKind;
using SdkSeverity = uk.osric.asp.Sdk.PageDiagnosticSeverity;

/// <summary>One layer's <see cref="IPageCheck"/>, bound to that render's deadline and abandonment.</summary>
/// <remarks>
/// <para>
/// A template is the save's front half without the store: generate the source, generate each library
/// in the shape a library is compiled in, wait for a compile slot, compile, and answer the mapped
/// diagnostics. A script is the template-blanking skeleton and the parser the assistant already checks
/// with. Either way it writes no revision, head, image, closure fingerprint or seed record, and it
/// recompiles none of a saved page's dependents, which is the whole point of asking without saving.
/// </para>
/// <para>
/// It is charged exactly what a save is charged: a slot from <see cref="PageBudget.CompileSlots"/>
/// and <see cref="CompileOptions.Timeout"/>. The wait, the compile and the library read all end at
/// the render's deadline or the compile timeout, whichever comes first, because a check has no
/// author's edit to protect: nothing is stored, so abandoning it loses nothing. It answers a
/// <c>PAGE010</c> error instead of throwing when that happens.
/// </para>
/// <para>
/// Records <c>page.compile.duration</c> under the calling page's key with the reason <c>check</c>.
/// </para>
/// </remarks>
internal sealed class PageCheckScope(
    PagePath path,
    IPageCompiler compiler,
    PageLibraries libraries,
    PageBudget budget,
    PageMetrics metrics,
    ILogger<PageCheckScope> logger) : RenderBoundCapability, IPageCheck {

    /// <inheritdoc/>
    public Task<PageCheckResult> CheckAsync(string language, string source) {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(source);

        if (!PageCheckLanguages.TryParse(language, out PageCheckLanguage named)) {
            throw new ArgumentException(
                $"'{language}' is not a language a check names; a check names one of "
                + $"{string.Join(", ", PageCheckLanguages.Names)}.",
                nameof(language));
        }

        return named switch {
            PageCheckLanguage.Template => CheckTemplateAsync(source),
            PageCheckLanguage.JavaScript => Task.FromResult(CheckScript(source)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(language), named, "This build has no checker for that language."),
        };
    }

    // A script page is still a template: the skeleton blanks every construct and keeps the line
    // breaks, so the parser's positions are the author's. A skeleton that cannot be built answers its
    // own diagnostic, so an unterminated block says so instead of failing the parse.
    private static PageCheckResult CheckScript(string script) {
        ScriptSkeletonResult skeleton = TemplateScriptSkeleton.Create(script);
        IReadOnlyList<CoreDiagnostic> found = skeleton.IsSuccess
            ? JavaScriptValidator.Validate(skeleton.Source)
            : skeleton.Diagnostics;

        IReadOnlyList<SdkDiagnostic> mapped = PageDiagnostics.ToSdk(found);

        return new PageCheckResult(
            // A script has no page kind: generation never ran, so there is nothing to judge.
            Kind: null,
            mapped.All(diagnostic => diagnostic.Severity != SdkSeverity.Error),
            mapped);
    }

    private async Task<PageCheckResult> CheckTemplateAsync(string template) {
        long start = Stopwatch.GetTimestamp();

        // An abandoned render cannot spend a compile: the page that asked is unwinding and nothing
        // can read the answer.
        if (Stopped) {
            return Unfinished("the check was given up on before it started");
        }

        TemplateGenerationResult generated = TemplateSourceGenerator.Generate(template);
        IReadOnlyList<CoreDiagnostic> shape = TemplateShapeCheck.Check(template);

        // Nothing to compile, so no slot is waited for and Roslyn is never called. Generation's
        // verdict and the shape check are the whole answer.
        if (generated.Kind is CoreKind.Text && generated.Source is not null) {
            return Answer(generated.Kind, shape);
        }

        // A template that does not parse never reaches the compiler; its generation errors are the
        // answer, exactly as a save stores them.
        if (generated.Source is not { } pageSource) {
            return Answer(generated.Kind, generated.Errors);
        }

        // A library contributes its declarations and no handler, so it is generated again in that
        // shape: the page's unit carries a handler nothing can run.
        GeneratedSource source = generated.Kind is CoreKind.Library
            ? TemplateSourceGenerator.GenerateLibrary(template).Source!
            : pageSource;

        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Deadline);
        stop.CancelAfter(budget.CompileOptions.Timeout);

        IReadOnlyList<LibrarySource> candidates;
        try {
            candidates = PageLibraries.Sources(await libraries.LiveAsync(stop.Token), logger, "a check");
        } catch (OperationCanceledException) {
            string why = Stopped
                ? "the check was given up on while it read the libraries"
                : "the check ran out of time before it could compile";
            logger.LogWarning("Checking a template for {Path} stopped before compiling: {Reason}", path.Key, why);
            return Unfinished(why);
        }

        bool queued;
        try {
            queued = await budget.CompileSlots.WaitAsync(Timeout.InfiniteTimeSpan, stop.Token);
        } catch (OperationCanceledException) {
            string why = Stopped
                ? "the check was given up on while it waited for a compile slot"
                : $"the check waited longer than {PageBudget.DescribeSeconds(budget.CompileOptions.Timeout)} s for a compile slot";
            logger.LogWarning("Checking a template for {Path} stopped: {Reason}", path.Key, why);
            return Unfinished(why);
        }

        if (!queued) {
            return Unfinished("the check could not take a compile slot");
        }

        CompileResult result;
        try {
            try {
                result = generated.Kind is CoreKind.Library
                    ? compiler.CompileWorker(source, candidates, stop.Token)
                    : compiler.Compile(source, candidates, stop.Token);
            } catch (OperationCanceledException) {
                string why = Stopped
                    ? "the check was given up on before it finished compiling"
                    : $"the check took longer than {PageBudget.DescribeSeconds(budget.CompileOptions.Timeout)} s to compile";
                logger.LogWarning("Checking a template for {Path} stopped: {Reason}", path.Key, why);
                return Unfinished(why);
            }
        } finally {
            budget.CompileSlots.Release();
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordCompile(path, result.IsSuccess, PageMetrics.ReasonCheck, duration);

        if (!result.IsSuccess) {
            logger.LogDebug("Generated source for a check for {Path}:{NewLine}{Source}", path.Key, Environment.NewLine, source.Text);
        }

        logger.LogDebug("Checked a template for {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Key,
            duration.TotalMilliseconds, result.IsSuccess ? "ok" : "error");

        // The compiler refines a declaration-only source: a library stays a library unless it declares
        // one entry point, which makes it a worker.
        return Answer(result.Kind ?? generated.Kind, [.. shape, .. result.Diagnostics]);
    }

    private static PageCheckResult Answer(CoreKind kind, IEnumerable<CoreDiagnostic> diagnostics) {
        IReadOnlyList<SdkDiagnostic> mapped = PageDiagnostics.ToSdk(diagnostics);

        return new PageCheckResult(
            KindOf(kind),
            mapped.All(diagnostic => diagnostic.Severity != SdkSeverity.Error),
            mapped);
    }

    // An abandoned render and an expiring compile budget are different facts that answer the one
    // code, so the sentence says which happened; the caller reads PAGE010 either way.
    private bool Stopped => IsAbandoned || Deadline.IsCancellationRequested;

    private static PageCheckResult Unfinished(string why) =>
        new(null, false, PageDiagnostics.ToSdk([
            new CoreDiagnostic(CoreSeverity.Error, null, $"PAGE010: {why}."),
        ]));

    private static SdkKind KindOf(CoreKind kind) => kind switch {
        CoreKind.Text => SdkKind.Text,
        CoreKind.Page => SdkKind.Page,
        CoreKind.Library => SdkKind.Library,
        CoreKind.Worker => SdkKind.Worker,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No page kind is declared for that value."),
    };
}
