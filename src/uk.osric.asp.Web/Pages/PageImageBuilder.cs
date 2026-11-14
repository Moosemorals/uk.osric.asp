// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;

/// <summary>Turns a page template into the image stored beside it.</summary>
/// <remarks>
/// <para>
/// This is the Web half of compilation: generate a <see cref="GeneratedSource"/>, wait for a
/// compile slot, bound the compile, and turn the result into a <see cref="PageImage"/>. It never
/// touches storage, so a save and a lazy compile share it without either owning the other.
/// </para>
/// <para>
/// Records <c>page.compile.duration</c> and <c>page.image.size</c>.
/// </para>
/// </remarks>
internal sealed class PageImageBuilder(
    IPageCompiler compiler,
    PageBudget budget,
    PageMetrics metrics,
    ILogger<PageImageBuilder> logger) {

    public string RuntimeId => compiler.RuntimeId;

    // One deadline covers queueing for a slot and compiling, the same way a render's budget covers
    // queueing and running. An unbounded Wait would park a request thread - and the render slot it
    // already holds - for as long as the compiler stays busy, with nothing to shed the request.
    //
    // A wait for a slot is abandoned at the render's deadline too when there is one: nothing has been
    // compiled, so nothing is lost and the next request tries again. The compile itself is left to
    // finish and store its image (DEC-43).
    public PageImage Build(
        PagePath path,
        string template,
        string reason,
        IReadOnlyList<PageLibrary> libraries,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline = default) {
        ArgumentNullException.ThrowIfNull(libraries);

        long start = Stopwatch.GetTimestamp();
        TemplateGenerationResult generated = TemplateSourceGenerator.Generate(template);
        IReadOnlyList<PageDiagnostic> shape = TemplateShapeCheck.Check(template);

        // Nothing to compile, so no slot is waited for and Roslyn is never called. The revision keeps
        // the verdict beside its source, and every request writes the source out as it is.
        if (generated.Kind is PageKind.Text && generated.Source is not null) {
            logger.LogDebug("Stored {Path} without compiling: the template has no code", path.Key);

            return new PageImage(
                TemplateFingerprint.Of(template),
                compiler.RuntimeId,
                Bytes: null,
                Symbols: null,
                Diagnostics: shape,
                Kind: PageKind.Text);
        }

        // A template that does not parse never reaches the compiler, but it fails exactly like a
        // syntax error: stored with its diagnostics, so the editor shows the fix and the page is not
        // recompiled on every request.
        CompileResult result;
        if (generated.Source is { } generatedSource) {
            // A library contributes its declarations and no handler, so it is generated again in that
            // shape: the page's unit carries a handler nothing can run, and the library's rule would
            // rightly refuse it.
            GeneratedSource source = generated.Kind is PageKind.Library
                ? TemplateSourceGenerator.GenerateLibrary(template).Source!
                : generatedSource;

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(budget.CompileOptions.Timeout);

            // The wait for a slot ends at whichever deadline comes first. Only the wait is linked to
            // the render's deadline: the compile keeps its own, so a compile that arrives late is
            // still stored rather than thrown away.
            using CancellationTokenSource queueDeadline =
                CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, renderDeadline);

            bool queued;
            try {
                queued = budget.CompileSlots.Wait(Timeout.InfiniteTimeSpan, queueDeadline.Token);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !renderDeadline.IsCancellationRequested) {
                // This compile's own timeout, not the caller giving up: fall through and report it.
                queued = false;
            }

            if (!queued) {
                // The site shed the work rather than park the request: a Warning, because the page
                // that caused it is the owner's to fix and nothing here can make the compiler faster.
                logger.LogWarning("Compiling {Path} waited longer than {TimeoutSeconds} s for a compile slot and was given up on",
                    path.Key, PageBudget.DescribeSeconds(budget.CompileOptions.Timeout));
                result = new CompileResult(null, null, [
                    new PageDiagnostic(PageDiagnosticSeverity.Error, null,
                        $"PAGE003: the page waited longer than {PageBudget.DescribeSeconds(budget.CompileOptions.Timeout)} s for a compile slot."),
                ]);
            } else {
                try {
                    try {
                        // A library is checked, never emitted: there is nothing to run, and the
                        // answer the author needs is its diagnostics.
                        result = generated.Kind is PageKind.Library
                            ? compiler.CompileLibrary(source, deadline.Token)
                            : compiler.Compile(source, Candidates(path, libraries), deadline.Token);
                    } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                        // Roslyn honours the token between operations, so this is a coarse bound: it is
                        // stored like any other page error so the editor shows it and the page is not
                        // recompiled on every request.
                        logger.LogWarning("Compiling {Path} took longer than {TimeoutSeconds} s and was stopped",
                            path.Key, PageBudget.DescribeSeconds(budget.CompileOptions.Timeout));
                        result = new CompileResult(null, null, [
                            new PageDiagnostic(PageDiagnosticSeverity.Error, null,
                                $"PAGE004: the page took longer than {PageBudget.DescribeSeconds(budget.CompileOptions.Timeout)} s to compile."),
                        ]);
                    } catch (InvalidOperationException ex) {
                        logger.LogDebug("Generated source for {Path}:{NewLine}{Source}", path.Key, Environment.NewLine, generatedSource);
                        logger.LogError(ex, "The generated source for {Path} did not compile; this is a compiler fault", path.Key);
                        metrics.RecordCompile(path, false, reason, Stopwatch.GetElapsedTime(start));
                        throw;
                    }
                } finally {
                    budget.CompileSlots.Release();
                }

                if (!result.IsSuccess) {
                    logger.LogDebug("Generated source for {Path}:{NewLine}{Source}", path.Key, Environment.NewLine, generatedSource);
                }
            }
        } else {
            result = new CompileResult(null, null, generated.Errors);
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordCompile(path, result.IsSuccess, reason, duration);
        if (result.Image is { } image) {
            metrics.RecordImageSize(path, image.Length);
        }

        logger.LogDebug("Compiled {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Key, duration.TotalMilliseconds,
            result.IsSuccess ? "ok" : "error");

        return new PageImage(
            TemplateFingerprint.Of(template),
            compiler.RuntimeId,
            result.Image,
            result.Symbols,
            PageDiagnostic.SortedForDisplay([.. shape, .. result.Diagnostics]),
            generated.Kind,
            Fingerprint(result.Libraries, libraries));
    }

    // The closure is the compiler's answer, and the hashes are the caller's: together they say what
    // this image was built against, which is what the next render compares with the live libraries.
    private static string? Fingerprint(IReadOnlyList<string> used, IReadOnlyList<PageLibrary> libraries) {
        if (used.Count == 0) {
            return null;
        }

        Dictionary<string, string> hashes = libraries
            .GroupBy(library => library.Key.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Hash, StringComparer.Ordinal);

        return PageLibraryFingerprint.Of(
            used.Where(hashes.ContainsKey).Select(key => (Key: key, Hash: hashes[key])));
    }

    // A library is contributed in its own shape, without the handler a page's unit carries. A row
    // that says library but no longer parses is left out: the page that names its namespace then
    // fails with the compiler's own missing-type error, which is a diagnostic rather than a 500.
    private IReadOnlyList<LibrarySource> Candidates(PagePath path, IReadOnlyList<PageLibrary> libraries) {
        List<LibrarySource> candidates = [];

        foreach (PageLibrary library in libraries) {
            try {
                TemplateGenerationResult generated = TemplateSourceGenerator.GenerateLibrary(library.Template);
                if (generated.Source is { } source) {
                    candidates.Add(new LibrarySource(library.Key.Key, source));
                } else {
                    logger.LogWarning("Library {Library} could not be generated, so it was left out of {Path}",
                        library.Key.Key, path.Key);
                }
            } catch (ArgumentException ex) {
                logger.LogWarning(ex, "Library {Library} is not declarations, so it was left out of {Path}",
                    library.Key.Key, path.Key);
            }
        }

        return candidates;
    }
}
