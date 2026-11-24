// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using System.Diagnostics.CodeAnalysis;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;
// A job judges Core's diagnostics and page kind; the page contract has its own of each.
using PageDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;
using PageDiagnosticSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;
using PageKind = uk.osric.asp.Core.Pages.PageKind;

/// <summary>Runs queued jobs, a bounded number at a time, each with its own scope and load context.</summary>
/// <remarks>
/// <para>
/// A job takes a job slot rather than a render slot, because it is not answering a visitor: the runner
/// keeps <c>Pages:Jobs:MaxConcurrent</c> of them going and the queue's own bound is what refuses the
/// next start. Each job gets a scope of its own, so the page store it reads is its own, and the
/// worker's assembly is unloaded when its job ends.
/// </para>
/// <para>
/// The worker's image is loaded the way a page's is: the stored image is used when it is current, and
/// rebuilt when the build changed or a library it named has moved. A worker that no longer compiles,
/// or is no longer stored as one, is a failed job with the reason, never a silent one.
/// </para>
/// </remarks>
internal sealed class PageJobRunner(
    PageJobQueue queue,
    PageJobs jobs,
    PageJobEnvironment environment,
    ILoggerFactory loggerFactory,
    ILogger<PageJobRunner> logger) : BackgroundService {

    private readonly List<Task> _running = [];
    private readonly SemaphoreSlim _slots = new(
        environment.Jobs.MaxConcurrent, environment.Jobs.MaxConcurrent);

    /// <inheritdoc/>
    public override void Dispose() {
        _slots.Dispose();
        base.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            await foreach (PageJobRequest request in queue.ReadAllAsync(stoppingToken)) {
                await _slots.WaitAsync(stoppingToken);

                _running.RemoveAll(task => task.IsCompleted);
                _running.Add(RunOneAsync(request, stoppingToken));
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
        }

        // A job is cancelled by the stopping token, so these end rather than run for the ceiling.
        await Task.WhenAll(_running);
    }

    private async Task RunOneAsync(PageJobRequest request, CancellationToken stoppingToken) {
        try {
            await RunAsync(request, stoppingToken);
        } finally {
            _ = _slots.Release();
        }
    }

    private async Task RunAsync(PageJobRequest request, CancellationToken stoppingToken) {
        PageJob? job = jobs.Find(request.Id);
        if (job is null) {
            return;
        }

        job.MarkRunning();

        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal) {
            ["JobId"] = job.Id,
            ["Worker"] = job.Worker.Key,
            ["Page"] = job.Page.Key,
        });

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(job.Lifetime.Token, stoppingToken);

        // The writer needs the scoped store a render's writer has, so the job opens one scope and
        // keeps it for its life: a per-call scope could not outlive a transaction that spans calls.
        await using AsyncServiceScope services = environment.Scopes.CreateAsyncScope();
        PageEditor editor = services.ServiceProvider.GetRequiredService<PageEditor>();

        logger.LogInformation("Job {JobId} started on worker {Worker}", job.Id, job.Worker.Key);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        try {
            PageTemplate? page = await environment.Store.GetAsync(job.Worker, linked.Token);

            if (page is null || page.Kind is not PageKind.Worker) {
                Finish(job, PageJobState.Failed, "The worker is no longer stored.", start);

                return;
            }

            PageImage? image = await ImageAsync(page, linked.Token);
            if (image?.Bytes is null) {
                Finish(job, PageJobState.Failed, Describe(image?.Diagnostics), start);

                return;
            }

            using LoadedWorker worker = environment.Compiler.LoadWorker(image.Bytes, image.Symbols);
            IWorker instance = worker.Create();

            await using WorkerContext context = Context(job, editor, linked.Token);
            await instance.RunAsync(context);

            // A worker that returns is not always a job that finished: one that ignored its token and
            // outran a clock has been cut, and the owner is owed the reason.
            (PageJobState state, string? failure) = Cut(job);
            Finish(job, state, failure, start);
        } catch (OperationCanceledException) when (linked.IsCancellationRequested) {
            (PageJobState state, string? failure) = stoppingToken.IsCancellationRequested
                ? (PageJobState.Cancelled, null)
                : Cut(job);

            Finish(job, state, failure, start);
        } catch (Exception exception) {
            logger.LogError(exception, "Job {JobId} failed", job.Id);
            Finish(job, PageJobState.Failed, exception.Message, start);
        } finally {
            job.Dispose();
        }
    }

    // A job that outran a clock was cut, which is a failure rather than a cancellation: nobody asked
    // it to stop, and the reason is the owner's to read.
    private (PageJobState State, string? Failure) Cut(PageJob job) => job.Lifetime.End switch {
        JobEnd.Cancelled => (PageJobState.Cancelled, null),
        JobEnd.Silence => (PageJobState.Failed,
            $"The job published and received nothing for {PageBudget.DescribeSeconds(environment.Jobs.Silence)} s."),
        JobEnd.Ceiling => (PageJobState.Failed,
            $"The job ran for longer than {PageBudget.DescribeSeconds(environment.Jobs.MaxDuration)} s."),
        _ => (PageJobState.Completed, null),
    };

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The context owns the scope and disposes it when the job returns.")]
    private WorkerContext Context(PageJob job, PageEditor editor, CancellationToken token) {
        // A job has no session and no browser: its Visitor subject is the starter's account, and only
        // Person (and NextRead) lifetimes are allowed. For(workerKey) is the worker's own namespace.
        PageReaderSubject reader = PageReaderSubject.ForJob(environment.Stores.Visitor, job.Owner.Name);
        PageStoresScope stores = new(
            job.Page,
            reader,
            environment.StoreValues,
            environment.Messages,
            environment.Reserved,
            environment.SessionStore,
            environment.Stores,
            environment.ReadStore,
            writable: true,
            commit: null,
            environment.Records,
            objects: environment.Objects,
            secrets: environment.Secrets,
            activity: job.Lifetime.ReArm);
        stores.AttachDeadline(token);
        FetchBudget fetchBudget = new(
            environment.Render.BurstFor(environment.Jobs.FetchesPerSecond),
            environment.Jobs.FetchesPerSecond,
            environment.Clock,
            PageJobOptions.FetchRateKey);
        PageHttpFetch fetch = new(
            job.Worker,
            isHead: false,
            token,
            environment.Fetcher,
            fetchBudget,
            environment.Metrics,
            loggerFactory.CreateLogger(SimpleContext.PageCategory));
        ReadBudget readBudget = new(
            environment.Render.BurstFor(environment.Jobs.SourceBytesPerSecond),
            environment.Jobs.SourceBytesPerSecond,
            environment.Clock,
            PageJobOptions.ReadRateKey);
        PageCatalogScope catalog = new(
            job.Worker,
            // A worker has no request to return to and no visitor to show a menu: it has no links.
            [],
            environment.ReadStore,
            environment.Read,
            environment.Search,
            readBudget,
            environment.Reserved,
            environment.ContentTypes,
            environment.Seeds,
            environment.Compiler.RuntimeId);
        PageJobsScope jobScope = new(
            job.Page, isHead: false, job.Authority, jobs, environment.Jobs, job.Lifetime.ReArm);

        // The writer is the same capability a page gets, bound to the starting page and to the
        // credential that started the job: every commit asks that credential before it stores.
        PageWritesScope writes = new(
            job.Page,
            editor,
            job.Owner,
            writable: true,
            environment.Reserved,
            environment.Seeds,
            new PageTransactionGate(),
            job.Authority,
            job.Lifetime.ReArm);
        writes.AttachDeadline(token);

#pragma warning disable CS0618 // The obsolete adapter keeps a worker that has not been edited running.
        PageCapabilities capabilities = new PageCapabilities()
            .Add<ISiteInfo>(new SiteInfo(
                environment.SiteFacts,
                () => SiteBoundsSnapshot.ForJob(
                    fetchBudget,
                    readBudget,
                    job.Lifetime.Total,
                    job.Lifetime.Remaining)))
            .Add<IPageStorage>(new PageStorageScope(stores.PageValues))
            .Add<IHttpFetch>(fetch)
            .Add<IPageCatalog>(catalog)
            .Add<IPageJobs>(jobScope)
            .Add<IPageWrites>(writes);
#pragma warning restore CS0618

        // A job is one lifetime, so its capabilities stop when it does rather than when a render ends.
        capabilities.AttachDeadline(token);

        return new WorkerContext(job, capabilities, stores, environment.Jobs, logger);
    }

    // The stored image is used when the template, the build and the libraries it named are unchanged;
    // otherwise it is compiled again — with the libraries a worker's own using directives reach — and
    // stored, exactly as a page's lazy compile is.
    private async Task<PageImage?> ImageAsync(PageTemplate page, CancellationToken token) {
        string? closure = await ClosureAsync(page, token);
        string hash = TemplateFingerprint.Of(page.Template);

        if (page.Image is { } stored && stored.IsCurrent(hash, environment.Compiler.RuntimeId, closure)) {
            return stored;
        }

        IReadOnlyList<PageLibrary> available = await environment.Libraries.LiveAsync(token);
        PageImage built = environment.Images.Build(
            page.Path, page.Template, PageMetrics.ReasonLazy, available, token);

        logger.LogDebug("Rebuilt worker {Worker} before running it", page.Path.Key);

        return await environment.Store.TrySetImageAsync(page.RevisionId, page.Template, built, token) ? built : null;
    }

    private async Task<string?> ClosureAsync(PageTemplate page, CancellationToken token) {
        if (string.IsNullOrEmpty(page.Image?.Libraries)) {
            return null;
        }

        IReadOnlyList<PageLibraryHead> heads = await environment.Libraries.HeadsAsync(token);

        return PageLibraryFingerprint.Of(heads.Select(head => (head.Key.Key, head.Hash)));
    }

    private static string Describe(IReadOnlyList<PageDiagnostic>? diagnostics) {
        if (diagnostics is null) {
            return "The worker's image could not be stored.";
        }

        PageDiagnostic? error = diagnostics.FirstOrDefault(
            diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);

        return error?.Message ?? "The worker did not compile.";
    }

    private void Finish(PageJob job, PageJobState state, string? failure, long start) {
        job.End(state, failure);

        TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
        if (state is PageJobState.Failed) {
            logger.LogWarning("Job {JobId} {State} after {Seconds:0.0} s: {Failure}",
                job.Id, state, elapsed.TotalSeconds, failure);
        } else {
            logger.LogInformation("Job {JobId} {State} after {Seconds:0.0} s",
                job.Id, state, elapsed.TotalSeconds);
        }
    }
}
