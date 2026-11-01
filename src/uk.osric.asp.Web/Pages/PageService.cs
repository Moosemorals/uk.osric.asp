// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;

/// <summary>Compiles, stores and renders pages.</summary>
/// <remarks>
/// A page is compiled when it is saved. A page whose stored image is missing, was built from other
/// content, or was built by another build is compiled on its next request and stored, so a page is
/// never unrunnable just because it did not arrive through the editor.
/// </remarks>
public sealed class PageService(
    IStorage storage,
    ITemplateCompiler compiler,
    PageMetrics metrics,
    PageBudget budget,
    PageHealth health,
    ILogger<PageService> logger) {

    // A page can be saved while its previous version is compiling; each attempt re-reads the newer row.
    private const int MaxCompileAttempts = 3;

    /// <summary>Reads a page for the editor without compiling it.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<StoredPage?> GetAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.GetPageAsync(path, cancellationToken);
    }

    /// <summary>Compiles a page and stores its source and result together.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="content">The template source.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The compiler's author-facing diagnostics, errors and warnings; empty when the page compiled cleanly.</returns>
    public async Task<IReadOnlyList<PageDiagnostic>> SaveAsync(PagePath path, string content, CancellationToken cancellationToken = default) {
        long start = Stopwatch.GetTimestamp();

        PageCompilation compilation;
        try {
            compilation = Compile(path, content, PageMetrics.ReasonSave, cancellationToken);
        } catch (InvalidOperationException) {
            // A compiler fault is our bug, but the author's edit must survive it. Store the source
            // without a compilation; the next request retries, and the failure still answers 500.
            await storage.SavePageAsync(new StoredPage(path, content), cancellationToken);
            metrics.RecordSave(path, false, Stopwatch.GetElapsedTime(start));
            throw;
        }

        await storage.SavePageAsync(new StoredPage(path, content, compilation), cancellationToken);

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordSave(path, compilation.Image is not null, duration);
        logger.LogInformation("Saved {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Path, duration.TotalMilliseconds,
            Outcome(compilation));

        return compilation.Diagnostics;
    }

    /// <summary>Runs a page, compiling and storing it first when its stored image cannot be used.</summary>
    /// <param name="path">The canonical key to render.</param>
    /// <param name="context">The context the page writes to.</param>
    /// <param name="cancellationToken">Cancels the read, any store, and the render itself.</param>
    /// <remarks>
    /// The budget covers queueing and running, because a visitor waits for both. A page that outruns
    /// it cannot be stopped, so the answer is given up on instead: the context is abandoned, the
    /// thread is counted against the site's health, and the response is a <see cref="RenderStatus.Timeout"/>.
    /// </remarks>
    public async Task<RenderResult> RenderAsync(PagePath path, SimpleContext context, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);

        long start = Stopwatch.GetTimestamp();
        string outcome = PageMetrics.RenderError;
        string cache = PageMetrics.CacheNone;

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget.RenderOptions.Timeout);

        try {
            long queueStart = Stopwatch.GetTimestamp();

            try {
                await budget.RenderSlots.WaitAsync(deadline.Token);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                // Queued for longer than the whole budget: shed the load rather than answer late.
                outcome = PageMetrics.RenderTimeout;
                metrics.RecordWait(path, Stopwatch.GetElapsedTime(queueStart));
                logger.LogWarning("Page {Path} waited {ElapsedMs:0} ms for a render slot and was dropped",
                    path.Path, Stopwatch.GetElapsedTime(queueStart).TotalMilliseconds);

                return RenderResult.TimedOut;
            }

            metrics.RecordWait(path, Stopwatch.GetElapsedTime(queueStart));

            try {
                LoadedPage? loaded = await LoadAsync(path, cancellationToken);
                if (loaded is null) {
                    outcome = PageMetrics.RenderNotFound;
                    return RenderResult.NotFound;
                }

                cache = loaded.Cache;

                if (loaded.Compilation.Image is null) {
                    outcome = PageMetrics.RenderCompileError;
                    return RenderResult.CompileFailed(loaded.Compilation.Diagnostics);
                }

                RenderResult rendered = await RunAsync(path, loaded.Compilation.Image, context, deadline, cancellationToken);
                outcome = rendered.Status == RenderStatus.Timeout ? PageMetrics.RenderTimeout : PageMetrics.RenderOk;

                return rendered;
            } finally {
                budget.RenderSlots.Release();
            }
        } finally {
            // Reaching here by exception leaves the outcome as `error`, which is what a dashboard
            // should see: the page was neither served nor reported as a compile failure.
            metrics.RecordRender(path, outcome, cache, Stopwatch.GetElapsedTime(start));
        }
    }

    private async Task<LoadedPage?> LoadAsync(PagePath path, CancellationToken cancellationToken) {
        LoadedPage? fallback = null;

        for (int attempt = 1; attempt <= MaxCompileAttempts; attempt++) {
            StoredPage? page = await storage.GetPageAsync(path, cancellationToken);
            if (page is null) {
                metrics.RecordCacheLookup(PageMetrics.LookupAbsent);
                return null;
            }

            string contentHash = ContentFingerprint.Of(page.Content);
            if (page.Compilation is { } stored && IsCurrent(stored, contentHash)) {
                metrics.RecordCacheLookup(PageMetrics.LookupHit);
                return new LoadedPage(stored, PageMetrics.CacheHit);
            }

            metrics.RecordStaleness(path, StalenessReason(page.Compilation));
            metrics.RecordCacheLookup(PageMetrics.LookupStale);

            PageCompilation compilation = Compile(path, page.Content, PageMetrics.ReasonLazy, cancellationToken);
            fallback = new LoadedPage(compilation, PageMetrics.CacheMiss);

            if (await storage.TrySetCompilationAsync(path, page.Content, compilation, cancellationToken)) {
                return fallback;
            }

            logger.LogDebug("Discarded a compile of {Path} because the page changed while it compiled", path.Path);
        }

        // The page kept changing; serving the last compile is better than failing the request.
        return fallback;
    }

    private bool IsCurrent(PageCompilation compilation, string contentHash) {
        return string.Equals(compilation.ContentHash, contentHash, StringComparison.Ordinal)
            && string.Equals(compilation.RuntimeId, compiler.RuntimeId, StringComparison.Ordinal);
    }

    private string StalenessReason(PageCompilation? compilation) {
        if (compilation is null) {
            return PageMetrics.StaleNeverCompiled;
        }

        return string.Equals(compilation.RuntimeId, compiler.RuntimeId, StringComparison.Ordinal)
            ? PageMetrics.StaleContentChanged
            : PageMetrics.StaleRuntimeChanged;
    }

    // The wait is blocking on purpose: compilation is synchronous, CPU-bound and short, and it is
    // bounded by the deadline the holder is running against. Compiling on a pool thread while the
    // request waits would hold the same thread for longer, with more moving parts.
    private PageCompilation Compile(PagePath path, string content, string reason, CancellationToken cancellationToken) {
        long start = Stopwatch.GetTimestamp();
        TemplateParseResult parse = TemplateSourceGenerator.GenerateTemplateSource(content);

        // A template that does not parse never reaches the compiler, but it fails exactly like a
        // syntax error: stored with its diagnostics, so the editor shows the fix and the page is not
        // recompiled on every request.
        CompileResult result;
        if (parse.Source is { } source) {
            budget.CompileSlots.Wait();

            try {
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(budget.CompileOptions.Timeout);

                try {
                    result = compiler.Compile(source, deadline.Token);
                } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                    // Roslyn honours the token between operations, so this is a coarse bound: it is
                    // stored like any other page error so the editor shows it and the page is not
                    // recompiled on every request.
                    result = new CompileResult(null, [
                        new PageDiagnostic(PageDiagnosticSeverity.Error, null,
                            $"PAGE002: the page took longer than {budget.CompileOptions.TimeoutSeconds} s to compile."),
                    ]);
                } catch (InvalidOperationException ex) {
                    logger.LogDebug("Generated source for {Path}:{NewLine}{Source}", path.Path, Environment.NewLine, source);
                    logger.LogError(ex, "The generated source for {Path} did not compile; this is a compiler fault", path.Path);
                    metrics.RecordCompile(path, false, reason, Stopwatch.GetElapsedTime(start));
                    throw;
                }
            } finally {
                budget.CompileSlots.Release();
            }

            if (!result.Success) {
                logger.LogDebug("Generated source for {Path}:{NewLine}{Source}", path.Path, Environment.NewLine, source);
            }
        } else {
            result = new CompileResult(null, parse.Errors);
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordCompile(path, result.Success, reason, duration);
        if (result.Image is { } image) {
            metrics.RecordImageSize(path, image.Length);
        }

        logger.LogDebug("Compiled {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Path, duration.TotalMilliseconds,
            result.Success ? "ok" : "error");

        return new PageCompilation(ContentFingerprint.Of(content), compiler.RuntimeId, result.Image, result.Diagnostics);
    }

    private static string Outcome(PageCompilation compilation) {
        if (compilation.Image is null) {
            return "with errors";
        }

        return compilation.Diagnostics.Count > 0 ? "compiled with warnings" : "compiled";
    }

    private async Task<RenderResult> RunAsync(
        PagePath path,
        byte[] image,
        SimpleContext context,
        CancellationTokenSource deadline,
        CancellationToken cancellationToken) {

        long loadStart = Stopwatch.GetTimestamp();
        CompiledTemplate template = compiler.Load(image);

        metrics.RecordLoad(path, Stopwatch.GetElapsedTime(loadStart));

        long invokeStart = Stopwatch.GetTimestamp();

        // The page runs off the request's thread so the wait can be bounded. If it outlives the
        // budget the task is abandoned rather than cancelled: nothing can stop arbitrary C#, and an
        // abandoned thread is honest about it instead of pretending the page stopped.
        Task page = Task.Run(() => template.Invoke(context), CancellationToken.None);
        Task expiry = Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);

        if (await Task.WhenAny(page, expiry) == page) {
            try {
                await page;
                metrics.RecordInvoke(path, true, Stopwatch.GetElapsedTime(invokeStart));

                return RenderResult.Rendered;
            } catch {
                metrics.RecordInvoke(path, false, Stopwatch.GetElapsedTime(invokeStart));
                throw;
            } finally {
                template.Dispose();
            }
        }

        Abandon(path, context, template, page, invokeStart, cancellationToken.IsCancellationRequested);

        return RenderResult.TimedOut;
    }

    private void Abandon(
        PagePath path,
        SimpleContext context,
        CompiledTemplate template,
        Task page,
        long invokeStart,
        bool disconnected) {

        TimeSpan elapsed = Stopwatch.GetElapsedTime(invokeStart);

        metrics.RecordInvoke(path, false, elapsed);
        metrics.RecordAbandoned(path);
        context.Abandon();
        health.Register();

        if (disconnected) {
            logger.LogWarning("Page {Path} was abandoned after {ElapsedMs:0} ms because the client went away",
                path.Path, elapsed.TotalMilliseconds);
        } else {
            logger.LogWarning("Page {Path} exceeded its {TimeoutSeconds} s budget after {ElapsedMs:0} ms; the render was abandoned",
                path.Path, budget.RenderOptions.TimeoutSeconds, elapsed.TotalMilliseconds);
        }

        // The template is released when the page finally stops, not now: unloading the assembly under
        // a running page would be worse than the leak. The health count drops with it, so a page that
        // returns late does not leave the site asking to be restarted.
        _ = page.ContinueWith(finished => {
            template.Dispose();
            health.Release();

            if (finished.Exception is { } failure) {
                logger.LogError(failure.GetBaseException(), "Abandoned page {Path} faulted after it was given up on", path.Path);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record LoadedPage(PageCompilation Compilation, string Cache);
}
