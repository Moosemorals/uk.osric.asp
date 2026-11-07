// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;

/// <summary>Runs a stored page, compiling and storing it first when its image cannot be used.</summary>
/// <remarks>
/// <para>
/// The render budget covers queueing and running, because a visitor waits for both. A page that
/// outruns it cannot be stopped, so the answer is given up on instead: the context is abandoned, the
/// render is counted against the site's health, and the response is a
/// <see cref="RenderStatus.Timeout"/>.
/// </para>
/// <para>
/// The handler is asynchronous, so a page that is waiting returns its thread to the pool and unwinds
/// at its next <c>await</c> when the deadline fires. A page that blocks synchronously is unchanged:
/// only abandonment and the container's restart escape it.
/// </para>
/// <para>
/// Records <c>page.render.*</c>, <c>page.load.*</c>, <c>page.invoke.*</c> and <c>page.cache.*</c>.
/// </para>
/// </remarks>
internal sealed class PageRenderer(
    IPageStore storage,
    IPageCompiler compiler,
    PageImageBuilder images,
    PageMetrics metrics,
    PageBudget budget,
    PageHealth health,
    ILogger<PageRenderer> logger) {

    // A page can be saved while its previous version is compiling; each attempt re-reads the newer head.
    private const int MaxCompileAttempts = 3;

    /// <summary>Runs a page, compiling and storing it first when its stored image cannot be used.</summary>
    /// <param name="path">The canonical key to render.</param>
    /// <param name="context">The context the page writes to.</param>
    /// <param name="cancellationToken">Cancels the read, any store, and the render itself.</param>
    internal async Task<RenderResult> RenderAsync(PagePath path, SimpleContext context, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);

        long start = Stopwatch.GetTimestamp();
        string outcome = PageMetrics.RenderError;
        string cache = PageMetrics.CacheNone;

        // Not `using`: RunAsync takes ownership of the deadline from the moment it is called, so that
        // an abandoned page's continuation can still register on the token. See RunAsync.
        CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget.RenderOptions.Timeout);

        // The page's own storage and fetch calls are cancelled with the render, not only with the
        // request. Attaching here, rather than inside RunAsync, keeps the token live for the whole
        // render even on the paths that never start the page.
        context.AttachDeadline(deadline.Token);

        bool handedToRun = false;

        try {
            long queueStart = Stopwatch.GetTimestamp();

            try {
                await budget.RenderSlots.WaitAsync(deadline.Token);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                // Queued for longer than the whole budget: shed the load rather than answer late.
                outcome = PageMetrics.RenderTimeout;
                metrics.RecordWait(Stopwatch.GetElapsedTime(queueStart));
                logger.LogWarning("Page {Path} waited {ElapsedMs:0} ms for a render slot and was dropped",
                    path.Key, Stopwatch.GetElapsedTime(queueStart).TotalMilliseconds);

                return RenderResult.TimedOut;
            }

            metrics.RecordWait(Stopwatch.GetElapsedTime(queueStart));

            try {
                CachedPage? loaded;
                try {
                    loaded = await LoadAsync(path, deadline.Token, cancellationToken);
                } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                    // The read outlived the budget, so the page never ran and there is no thread to
                    // give up on: the answer is the same 504 all the same.
                    outcome = PageMetrics.RenderTimeout;
                    logger.LogWarning("Page {Path} spent its {TimeoutSeconds} s budget before it ran and was dropped",
                        path.Key, PageBudget.DescribeSeconds(budget.RenderOptions.Timeout));

                    return RenderResult.TimedOut;
                }

                if (loaded is null) {
                    outcome = PageMetrics.RenderNotFound;
                    return RenderResult.NotFound;
                }

                cache = loaded.Cache;

                if (loaded.Image.Bytes is null) {
                    outcome = PageMetrics.RenderCompileError;
                    return RenderResult.CompileFailed(loaded.Image.Diagnostics);
                }

                handedToRun = true;
                RenderResult rendered = await RunAsync(
                    path, loaded.Image.Bytes, loaded.Image.Symbols, context, deadline, cancellationToken);
                outcome = rendered.Status == RenderStatus.Timeout ? PageMetrics.RenderTimeout : PageMetrics.RenderOk;

                return rendered;
            } finally {
                budget.RenderSlots.Release();
            }
        } finally {
            if (!handedToRun) {
                // Every path that never reaches RunAsync keeps the deadline: a dropped queue wait, a
                // read that spent the budget, a missing page, a compile error or a throw.
                deadline.Dispose();
            }

            // Reaching here by exception leaves the outcome as `error`, which is what a dashboard
            // should see: the page was neither served nor reported as a compile failure.
            metrics.RecordRender(path, outcome, cache, Stopwatch.GetElapsedTime(start));
        }
    }

    // The read is answered against the render's deadline, because the visitor is waiting for it. The
    // compile and its store are not: cancelling one throws the work away and leaves the page
    // unservable, so they are bounded by Pages:Compile:TimeoutSeconds and get to finish and be stored
    // even when the answer has already been given up on. A page whose image arrives that late is not
    // started at all - see RunAsync - so the next request is served from the store.
    private async Task<CachedPage?> LoadAsync(PagePath path, CancellationToken deadline, CancellationToken cancellationToken) {
        for (int attempt = 1; attempt <= MaxCompileAttempts; attempt++) {
            PageTemplate? page = await storage.GetPageAsync(path, deadline);
            if (page is null) {
                metrics.RecordCacheLookup(PageMetrics.LookupAbsent);
                return null;
            }

            string templateHash = TemplateFingerprint.Of(page.Template);
            if (page.Image is { } stored && stored.IsCurrent(templateHash, compiler.RuntimeId)) {
                metrics.RecordCacheLookup(PageMetrics.LookupHit);
                return new CachedPage(stored, PageMetrics.CacheHit);
            }

            metrics.RecordStaleness(path, StalenessReason(page.Image));
            metrics.RecordCacheLookup(PageMetrics.LookupStale);

            PageImage image = images.Build(path, page.Template, PageMetrics.ReasonLazy, cancellationToken, deadline);

            if (await storage.TrySetImageAsync(page.RevisionId, page.Template, image, cancellationToken)) {
                PageTemplate? current = await storage.GetPageAsync(path, deadline);
                if (current is null) {
                    return null;
                }

                if (current.RevisionId == page.RevisionId) {
                    return new CachedPage(image, PageMetrics.CacheMiss);
                }
            }

            logger.LogDebug("Discarded a compile of {Path} because its live revision changed while it compiled", path.Key);
        }

        return null;
    }

    private string StalenessReason(PageImage? image) {
        if (image is null) {
            return PageMetrics.StaleNeverCompiled;
        }

        return string.Equals(image.RuntimeId, compiler.RuntimeId, StringComparison.Ordinal)
            ? PageMetrics.StaleTemplateChanged
            : PageMetrics.StaleRuntimeChanged;
    }

    // RunAsync owns the deadline from here on. An abandoned page can reach an await after this method
    // has returned, and registering on a disposed token would throw ObjectDisposedException inside the
    // page instead of cancelling it - so the source is disposed when the page finished here, or by the
    // abandon continuation when it did not.
    private async Task<RenderResult> RunAsync(
        PagePath path,
        byte[] image,
        byte[]? symbols,
        SimpleContext context,
        CancellationTokenSource deadline,
        CancellationToken cancellationToken) {

        bool handedToAbandon = false;

        try {
            // A page that arrived compiled in time can still have spent the budget on the way here - a
            // lazy compile is bounded by the compile options, not by the render's budget - and starting
            // it now would race it against an already-cancelled delay and answer either way. The image
            // is stored by then, so the next request renders it.
            if (deadline.IsCancellationRequested) {
                return RenderResult.TimedOut;
            }

            long loadStart = Stopwatch.GetTimestamp();
            LoadedPage template = compiler.Load(image, symbols);

            metrics.RecordLoad(path, Stopwatch.GetElapsedTime(loadStart));

            long invokeStart = Stopwatch.GetTimestamp();

            // The page runs off the request's thread so the wait can be bounded. If it outlives the
            // budget the task is abandoned rather than cancelled: nothing can stop arbitrary C#, and
            // an abandoned render is honest about it instead of pretending the page stopped. An async
            // page that is waiting hands its thread back while it waits, so an abandoned render costs
            // a task rather than a thread.
            Task page = Task.Run(() => template.InvokeAsync(context), CancellationToken.None);
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

            if (page.IsCanceled
                && (deadline.IsCancellationRequested || cancellationToken.IsCancellationRequested)) {
                // The page stopped because we asked it to: the answer was already given, or the
                // visitor left. That is not a page fault, so it is not counted as one.
                template.Dispose();
                metrics.RecordInvoke(path, false, Stopwatch.GetElapsedTime(invokeStart));

                return RenderResult.TimedOut;
            }

            handedToAbandon = true;
            Abandon(path, context, template, page, deadline, invokeStart, cancellationToken.IsCancellationRequested);

            return RenderResult.TimedOut;
        } finally {
            if (!handedToAbandon) {
                deadline.Dispose();
            }
        }
    }

    private void Abandon(
        PagePath path,
        SimpleContext context,
        LoadedPage template,
        Task page,
        CancellationTokenSource deadline,
        long invokeStart,
        bool disconnected) {

        TimeSpan elapsed = Stopwatch.GetElapsedTime(invokeStart);

        metrics.RecordInvoke(path, false, elapsed);
        metrics.RecordAbandoned(path);
        context.Abandon();
        health.Register();

        if (disconnected) {
            logger.LogWarning("Page {Path} was abandoned after {ElapsedMs:0} ms because the client went away",
                path.Key, elapsed.TotalMilliseconds);
        } else {
            logger.LogWarning("Page {Path} exceeded its {TimeoutSeconds} s budget after {ElapsedMs:0} ms; the render was abandoned",
                path.Key, PageBudget.DescribeSeconds(budget.RenderOptions.Timeout), elapsed.TotalMilliseconds);
        }

        // The template is released when the page finally stops, not now: unloading the assembly under
        // a running page would be worse than the leak. The health count drops with it, so a page that
        // returns late does not leave the site asking to be restarted.
        _ = page.ContinueWith(finished => {
            template.Dispose();
            deadline.Dispose();
            health.Release();

            if (finished.Exception is { } failure) {
                logger.LogError(failure.GetBaseException(), "Abandoned page {Path} faulted after it was given up on", path.Key);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record CachedPage(PageImage Image, string Cache);
}
