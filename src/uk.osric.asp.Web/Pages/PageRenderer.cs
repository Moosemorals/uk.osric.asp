// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;

/// <summary>A revision the caller has already read, and why it was read.</summary>
/// <param name="Page">The revision to run.</param>
/// <param name="IsHistorical">
/// Whether the revision came from a <c>?v=</c> hash rather than the live head. A historical row is
/// fixed, so a compile is stored against it and the head is never read again.
/// </param>
internal sealed record PageSelection(PageTemplate Page, bool IsHistorical);

/// <summary>Runs a stored page, compiling and storing it first when its image cannot be used.</summary>
/// <remarks>
/// <para>
/// The render budget covers queueing and running, because a visitor waits for both. A page that
/// outruns it cannot be stopped, so the answer is given up on instead: the context is abandoned, the
/// render is counted against the site's health, and the response is a
/// <see cref="RenderStatus.Timeout"/>.
/// </para>
/// <para>
/// A page wrapped in a layout is several renders of one request, so the slot and the deadline are
/// taken once by a <see cref="RenderSession"/> and every layer renders under it. Otherwise a
/// two-layer page would cost two slots and hold two deadlines.
/// </para>
/// <para>
/// The handler is asynchronous, so a page that is waiting returns its thread to the pool and unwinds
/// at its next <c>await</c> when the deadline fires. A page that blocks synchronously is unchanged:
/// only abandonment and the container's restart escape it.
/// </para>
/// <para>
/// A revision the caller has already read can be run without reading the head again, which is how a
/// versioned URL selects one retained revision. A revision with no code is written from its stored
/// source and never compiled, loaded or invoked.
/// </para>
/// <para>
/// Records <c>page.render.*</c>, <c>page.load.*</c>, <c>page.invoke.*</c> and <c>page.cache.*</c>.
/// </para>
/// </remarks>
internal sealed class PageRenderer(
    IPageStore storage,
    IPageCompiler compiler,
    PageImageBuilder images,
    PageLibraries libraries,
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
        await using RenderSession session = await OpenSessionAsync(path, cancellationToken);

        return await session.RenderAsync(path, context);
    }

    /// <summary>Reads a page's live head, for an endpoint that must judge it before opening a render.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    internal Task<PageTemplate?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);

        return storage.GetPageAsync(path, cancellationToken);
    }

    /// <summary>Reads a revision by its hash, for a versioned URL.</summary>
    /// <param name="hash">The revision's stored hash.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    internal Task<PageTemplate?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(hash);

        return storage.GetRevisionByHashAsync(hash, cancellationToken);
    }

    /// <summary>Runs a page the caller has already read, rather than reading its head again.</summary>
    /// <param name="path">The canonical key the request named.</param>
    /// <param name="context">The context the page writes to.</param>
    /// <param name="selection">The revision to run, and whether it came from a versioned URL.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    internal async Task<RenderResult> RenderAsync(
        PagePath path,
        SimpleContext context,
        PageSelection selection,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(selection);

        await using RenderSession session = await OpenSessionAsync(path, cancellationToken);

        return await session.RenderAsync(path, context, selection);
    }

    /// <summary>Takes one render slot and one deadline for every layer of a page and its layouts.</summary>
    /// <param name="path">The page the visitor asked for, for the log line and the metric.</param>
    /// <param name="cancellationToken">Cancels the wait and every render under the session.</param>
    /// <returns>
    /// The session to render the layers under; disposing it releases the slot. A session whose slot
    /// wait outlived the budget renders nothing and answers <see cref="RenderStatus.Timeout"/>.
    /// </returns>
    internal async Task<RenderSession> OpenSessionAsync(PagePath path, CancellationToken cancellationToken = default) {
        long start = Stopwatch.GetTimestamp();

        // Not `using`: RunAsync takes ownership of the deadline from the moment it is called, so that
        // an abandoned page's continuation can still register on the token. See RunLayerAsync.
        CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget.RenderOptions.Timeout);

        long queueStart = Stopwatch.GetTimestamp();

        try {
            await budget.RenderSlots.WaitAsync(deadline.Token);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // Queued for longer than the whole budget: shed the load rather than answer late.
            TimeSpan waited = Stopwatch.GetElapsedTime(queueStart);
            metrics.RecordWait(waited);
            metrics.RecordRender(path, PageMetrics.RenderTimeout, PageMetrics.CacheNone, Stopwatch.GetElapsedTime(start));
            logger.LogWarning("Page {Path} waited {ElapsedMs:0} ms for a render slot and was dropped",
                path.Key, waited.TotalMilliseconds);

            deadline.Dispose();
            return new RenderSession(this, deadline: null, slotHeld: false, cancellationToken);
        }

        metrics.RecordWait(Stopwatch.GetElapsedTime(queueStart));

        return new RenderSession(this, deadline, slotHeld: true, cancellationToken);
    }

    // Releasing here rather than in RenderSession keeps the semaphore private to the renderer: a
    // session only says whether the slot it was given is still held.
    private void CloseSession(bool slotHeld, CancellationTokenSource? deadline, bool abandoned) {
        if (slotHeld) {
            budget.RenderSlots.Release();
        }

        // An abandoned layer's continuation disposes the deadline once the page finally stops, so
        // doing it here would pull the token out from under a render that is still running.
        if (deadline is not null && !abandoned) {
            deadline.Dispose();
        }
    }

    private async Task<RenderResult> RenderLayerAsync(
        PagePath path,
        SimpleContext context,
        PageSelection? selection,
        CancellationTokenSource deadline,
        Action abandoned,
        CancellationToken cancellationToken) {

        long start = Stopwatch.GetTimestamp();
        string outcome = PageMetrics.RenderError;
        string cache = PageMetrics.CacheNone;

        try {
            CachedPage? loaded;
            try {
                loaded = await LoadAsync(path, selection, deadline.Token, cancellationToken);
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

            if (loaded.IsLibrary) {
                // A library has no representation to serve; its URL is answered by the endpoint, so
                // this is a layout or a chain asking for one.
                outcome = PageMetrics.RenderNotFound;
                return RenderResult.NotFound;
            }

            if (loaded.Source is { } source) {
                // The page has no code, so there is nothing to load or invoke: the stored source is
                // the body, byte for byte, and the render's body budget still applies.
                context.WriteRaw(source);
                outcome = PageMetrics.RenderOk;

                return RenderResult.FromSource;
            }

            if (loaded.Image?.Bytes is null) {
                outcome = PageMetrics.RenderCompileError;
                return RenderResult.CompileFailed(loaded.Image?.Diagnostics ?? []);
            }

            RenderResult rendered = await RunAsync(
                path, loaded.Image.Bytes, loaded.Image.Symbols, context, deadline, abandoned, cancellationToken);
            outcome = rendered.Status == RenderStatus.Timeout ? PageMetrics.RenderTimeout : PageMetrics.RenderOk;

            return rendered;
        } finally {
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
    private Task<CachedPage?> LoadAsync(
        PagePath path,
        PageSelection? selection,
        CancellationToken deadline,
        CancellationToken cancellationToken) {
        if (selection is { IsHistorical: true } historical) {
            return LoadRevisionAsync(path, historical.Page, deadline, cancellationToken);
        }

        return LoadHeadAsync(path, selection?.Page, deadline, cancellationToken);
    }

    private async Task<CachedPage?> LoadHeadAsync(
        PagePath path,
        PageTemplate? first,
        CancellationToken deadline,
        CancellationToken cancellationToken) {
        PageTemplate? page = first;

        for (int attempt = 1; attempt <= MaxCompileAttempts; attempt++) {
            if (page is null) {
                page = await storage.GetPageAsync(path, deadline);
            }

            if (page is null) {
                metrics.RecordCacheLookup(PageMetrics.LookupAbsent);
                return null;
            }

            if (FromStored(path, page, await ClosureAsync(page, deadline)) is { } stored) {
                return stored;
            }

            IReadOnlyList<PageLibrary> available = await libraries.LiveAsync(deadline);
            PageImage image = images.Build(
                path, page.Template, PageMetrics.ReasonLazy, available, cancellationToken, deadline);

            if (await storage.TrySetImageAsync(page.RevisionId, page.Template, image, cancellationToken)) {
                PageTemplate? current = await storage.GetPageAsync(path, deadline);
                if (current is null) {
                    return null;
                }

                if (current.RevisionId == page.RevisionId) {
                    return FromBuilt(page, image);
                }
            }

            logger.LogDebug("Discarded a compile of {Path} because its live revision changed while it compiled", path.Key);
            page = null;
        }

        return null;
    }

    // A retained revision cannot change under this read: the row and its source are fixed, and only
    // its image is filled in when it is first compiled.
    private async Task<CachedPage?> LoadRevisionAsync(
        PagePath path,
        PageTemplate page,
        CancellationToken deadline,
        CancellationToken cancellationToken) {
        if (FromStored(path, page, await ClosureAsync(page, deadline)) is { } stored) {
            return stored;
        }

        IReadOnlyList<PageLibrary> available = await libraries.LiveAsync(deadline);
        PageImage image = images.Build(
            path, page.Template, PageMetrics.ReasonLazy, available, cancellationToken, deadline);

        // The row is fixed, so a failed write means it disappeared while it compiled: a scrub is the
        // one thing that removes it, and a scrubbed revision must not be served.
        return await storage.TrySetImageAsync(page.RevisionId, page.Template, image, cancellationToken)
            ? FromBuilt(page, image)
            : null;
    }

    // A text template is served from source on every request and is never generated or compiled
    // again; an unjudged one is generated once by LoadHeadAsync, because it has never been judged.
    private CachedPage? FromStored(PagePath path, PageTemplate page, string? closure) {
        // A library is not a representation: nothing renders it, and its own URL answers 404 before
        // this runs. A layout that named one is not found rather than compiled as if it were a page.
        if (page.Kind is PageKind.Library) {
            metrics.RecordCacheLookup(PageMetrics.LookupHit);

            return new CachedPage(null, null, PageMetrics.CacheHit, IsLibrary: true);
        }

        if (page.Kind is PageKind.Text) {
            metrics.RecordCacheLookup(PageMetrics.LookupHit);

            return new CachedPage(null, page.Template, PageMetrics.CacheHit);
        }

        string templateHash = TemplateFingerprint.Of(page.Template);
        if (page.Image is { } stored && stored.IsCurrent(templateHash, compiler.RuntimeId, closure)) {
            metrics.RecordCacheLookup(PageMetrics.LookupHit);

            return stored.Kind is not PageKind.Text || stored.Bytes is not null
                ? new CachedPage(stored, null, PageMetrics.CacheHit)
                : new CachedPage(null, page.Template, PageMetrics.CacheHit);
        }

        metrics.RecordStaleness(path, StalenessReason(page.Image, closure));
        metrics.RecordCacheLookup(PageMetrics.LookupStale);

        return null;
    }

    private static CachedPage FromBuilt(PageTemplate page, PageImage image) => image.Kind switch {
        PageKind.Library => new CachedPage(null, null, PageMetrics.CacheMiss, IsLibrary: true),
        PageKind.Text => new CachedPage(null, page.Template, PageMetrics.CacheMiss),
        _ => new CachedPage(image, null, PageMetrics.CacheMiss),
    };

    private string StalenessReason(PageImage? image, string? closure) {
        if (image is null) {
            return PageMetrics.StaleNeverCompiled;
        }

        if (!string.Equals(image.RuntimeId, compiler.RuntimeId, StringComparison.Ordinal)) {
            return PageMetrics.StaleRuntimeChanged;
        }

        return string.Equals(image.Libraries ?? string.Empty, closure ?? string.Empty, StringComparison.Ordinal)
            ? PageMetrics.StaleTemplateChanged
            : PageMetrics.StaleLibraryChanged;
    }

    // A page that named no library cannot acquire one without its source changing, and a changed
    // source is already a miss, so only a page that named some needs this read. It is the heads and
    // not the sources: judging staleness must not read a library's text.
    private async Task<string?> ClosureAsync(PageTemplate page, CancellationToken deadline) {
        if (string.IsNullOrEmpty(page.Image?.Libraries)) {
            return null;
        }

        IReadOnlyList<PageLibraryHead> heads = await libraries.HeadsAsync(deadline);

        return PageLibraryFingerprint.Of(heads.Select(head => (head.Key.Key, head.Hash)));
    }

    // RunAsync hands the deadline to the abandon continuation when it gives up on a page. The
    // `abandoned` callback tells the session the same thing, so the session does not dispose a
    // token the abandoned page can still register on.
    private async Task<RenderResult> RunAsync(
        PagePath path,
        byte[] image,
        byte[]? symbols,
        SimpleContext context,
        CancellationTokenSource deadline,
        Action abandoned,
        CancellationToken cancellationToken) {

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

        Abandon(path, context, template, page, deadline, invokeStart, cancellationToken.IsCancellationRequested);
        abandoned();

        return RenderResult.TimedOut;
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

    private sealed record CachedPage(PageImage? Image, string? Source, string Cache, bool IsLibrary = false);

    /// <summary>One request's render slot and deadline, shared by every layer of its chain.</summary>
    internal sealed class RenderSession : IAsyncDisposable {

        private readonly CancellationToken _cancellationToken;
        private readonly PageRenderer _renderer;
        private readonly bool _slotHeld;
        private CancellationTokenSource? _deadline;
        private bool _abandoned;

        internal RenderSession(
            PageRenderer renderer,
            CancellationTokenSource? deadline,
            bool slotHeld,
            CancellationToken cancellationToken) {
            _renderer = renderer;
            _deadline = deadline;
            _slotHeld = slotHeld;
            _cancellationToken = cancellationToken;
        }

        /// <summary>Renders one layer of the chain under this session's slot and deadline.</summary>
        /// <param name="path">The layer's canonical key.</param>
        /// <param name="context">The context the layer writes to.</param>
        /// <param name="selection">Which representation to render, or <see langword="null"/> for the page's own.</param>
        internal async Task<RenderResult> RenderAsync(PagePath path, SimpleContext context, PageSelection? selection = null) {
            ArgumentNullException.ThrowIfNull(context);

            if (_deadline is not { } deadline) {
                // The slot was never taken: the request was already over before it started.
                return RenderResult.TimedOut;
            }

            // Attached here rather than when the session opened, so every layer's storage and fetch
            // calls are cancelled with the render and not only with the request.
            context.AttachDeadline(deadline.Token);

            return await _renderer.RenderLayerAsync(path, context, selection, deadline, () => _abandoned = true, _cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() {
            _renderer.CloseSession(_slotHeld, _deadline, _abandoned);
            _deadline = null;

            return ValueTask.CompletedTask;
        }
    }
}
