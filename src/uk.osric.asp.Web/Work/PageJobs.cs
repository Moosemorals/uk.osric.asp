// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using System.Collections.Concurrent;
using System.Diagnostics;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;
// The page contract has a page kind of its own; a job judges Core's.
using PageKind = uk.osric.asp.Core.Pages.PageKind;

/// <summary>The host's jobs: what a page starts, and what a viewer reads.</summary>
/// <remarks>
/// <para>
/// The registry is in memory and the queue is the existing bounded one, so a start is refused rather
/// than queued without bound, and a restart loses every job — a worker that is given the transcript
/// writes each step to the starting page's storage, which is the record, and the page that finds its
/// own state says running while <see cref="Get"/> answers nothing knows the host restarted.
/// </para>
/// <para>
/// Nothing here takes an owner argument from page code: every call is filtered against the visitor the
/// capability was bound to, so a page cannot read, talk to or stop another visitor's job. Every
/// refusal is a value, and every refusal is logged with the page that asked.
/// </para>
/// </remarks>
internal sealed class PageJobs(
    PageJobOptions options,
    PageJobQueue queue,
    ReservedPaths reserved,
    IPageReadStore readStore,
    TimeProvider clock,
    ILogger<PageJobs> logger) {

    private readonly ConcurrentDictionary<string, PageJob> _jobs = new(StringComparer.Ordinal);

    /// <summary>The bounds a caller's waits and messages are held to.</summary>
    public PageJobOptions Options => options;

    /// <summary>Starts one run of a stored worker.</summary>
    /// <param name="page">The page asking, for the log line and for the job's own storage.</param>
    /// <param name="workerKey">The worker's key, as the page spelled it.</param>
    /// <param name="firstMessage">The first message, or <see langword="null"/>.</param>
    /// <param name="authority">
    /// The credential the render presented, which the job acts with for its life, or
    /// <see langword="null"/> when the render is anonymous.
    /// </param>
    /// <param name="cancellationToken">Bounds the kind check, which is the only I/O here.</param>
    public async Task<PageJobStart> StartAsync(
        PagePath page,
        string workerKey,
        string? firstMessage,
        PageJobAuthority? authority,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(page);

        if (authority is null) {
            return RefuseStart(page, workerKey, PageJobRefusal.Anonymous, "the render is not signed in");
        }

        PagePath worker = PagePath.Normalize(workerKey);
        if (!worker.IsAddressable || string.Equals(worker.Key, "/", StringComparison.Ordinal)) {
            return RefuseStart(page, workerKey, PageJobRefusal.InvalidKey, "the key is not an addressable page key");
        }

        if (reserved.IsReserved(worker)) {
            return RefuseStart(page, worker.Key, PageJobRefusal.OwnedBySite, "the site's own routes answer that key");
        }

        if (firstMessage is not null && TooLarge(firstMessage)) {
            return RefuseStart(page, worker.Key, PageJobRefusal.MessageTooLarge, "the first message is too large");
        }

        PageKind? kind = await KindAsync(worker, cancellationToken);
        if (kind is null) {
            return RefuseStart(page, worker.Key, PageJobRefusal.UnknownWorker, "no page is stored there");
        }

        if (kind is not PageKind.Worker) {
            return RefuseStart(page, worker.Key, PageJobRefusal.NotAWorker, $"it is a {kind.Value.ToString().ToLowerInvariant()}");
        }

        if (!queue.TryReserve()) {
            return RefuseStart(page, worker.Key, PageJobRefusal.QueueFull, "the job queue is full");
        }

        Sweep();

        PageJob job = new(NewId(), worker, page, authority, firstMessage, options, clock);
        _jobs[job.Id] = job;

        if (!queue.TryEnqueueReserved(new PageJobRequest(job.Id, worker, page, authority))) {
            _ = _jobs.TryRemove(job.Id, out _);
            queue.ReleaseReservation();
            job.Dispose();

            return RefuseStart(page, worker.Key, PageJobRefusal.QueueFull, "the job queue is full");
        }

        logger.LogInformation("Page {Page} started job {JobId} on worker {Worker}", page.Key, job.Id, worker.Key);

        return new PageJobStart(job.Id, PageJobRefusal.None);
    }

    /// <summary>Queues one message for a job this visitor owns.</summary>
    /// <param name="page">The page asking, for the log line.</param>
    /// <param name="jobId">The job's id.</param>
    /// <param name="message">The message.</param>
    /// <param name="owner">The signed-in visitor.</param>
    public PageJobSendResult Send(PagePath page, string jobId, string message, PageUser? owner) {
        ArgumentNullException.ThrowIfNull(page);

        if (owner is null) {
            return RefuseSend(page, jobId, PageJobRefusal.Anonymous, "the render is not signed in");
        }

        if (message is null) {
            return RefuseSend(page, jobId, PageJobRefusal.MessageTooLarge, "the message is empty");
        }

        if (TooLarge(message)) {
            return RefuseSend(page, jobId, PageJobRefusal.MessageTooLarge, "the message is too large");
        }

        PageJob? job = Find(jobId, owner);
        if (job is null) {
            return RefuseSend(page, jobId, PageJobRefusal.UnknownJob, "the job is unknown or another visitor's");
        }

        PageJobRefusal refusal = job.TrySend(message);
        if (refusal is not PageJobRefusal.None) {
            return RefuseSend(page, jobId, refusal, "the job cannot take another message");
        }

        return new PageJobSendResult(PageJobRefusal.None);
    }

    /// <summary>Reads one job this visitor owns.</summary>
    /// <param name="jobId">The job's id.</param>
    /// <param name="owner">The signed-in visitor.</param>
    public PageJobInfo? Get(string jobId, PageUser? owner) => Find(jobId, owner)?.Snapshot();

    /// <summary>Lists this visitor's jobs, in the order they were started.</summary>
    /// <param name="owner">The signed-in visitor.</param>
    public IReadOnlyList<PageJobInfo> List(PageUser? owner) {
        if (owner is null) {
            return [];
        }

        Sweep();

        return [.. _jobs.Values
            .Where(job => string.Equals(job.Owner.Name, owner.Name, StringComparison.Ordinal))
            .OrderBy(job => job.StartedAt)
            .Select(job => job.Snapshot())];
    }

    /// <summary>Reads a job's events after a sequence, waiting briefly for the next one.</summary>
    /// <param name="jobId">The job's id.</param>
    /// <param name="after">The last sequence the caller has seen.</param>
    /// <param name="wait">How long to wait for something new.</param>
    /// <param name="owner">The signed-in visitor.</param>
    /// <param name="cancellationToken">Ends the wait when the render does.</param>
    public async Task<PageJobEvents> ReadEventsAsync(
        string jobId,
        long after,
        TimeSpan wait,
        PageUser? owner,
        CancellationToken cancellationToken) {
        PageJob? job = Find(jobId, owner);
        if (job is null) {
            // A job that is gone reads as nothing at all, which is what a state of null means.
            return new PageJobEvents([], after, Reset: false, State: null, Failure: null);
        }

        long start = Stopwatch.GetTimestamp();
        TimeSpan limit = wait > TimeSpan.Zero
            ? wait < options.MaxWait ? wait : options.MaxWait
            : TimeSpan.Zero;

        while (true) {
            PageJobJournal.Snapshot snapshot = job.Journal.Read(after);
            PageJobInfo info = job.Snapshot();

            bool ended = info.State is not (PageJobState.Queued or PageJobState.Running);
            if (snapshot.Events.Count > 0 || snapshot.Reset || ended) {
                return new PageJobEvents(snapshot.Events, snapshot.Next, snapshot.Reset, info.State, info.Failure);
            }

            TimeSpan remaining = limit - Stopwatch.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero) {
                return new PageJobEvents(snapshot.Events, snapshot.Next, snapshot.Reset, info.State, info.Failure);
            }

            try {
                await job.Journal.WaitAsync(remaining, cancellationToken);
            } catch (OperationCanceledException) {
                return new PageJobEvents(snapshot.Events, snapshot.Next, snapshot.Reset, info.State, info.Failure);
            }
        }
    }

    /// <summary>Asks a job to stop.</summary>
    /// <param name="page">The page asking, for the log line.</param>
    /// <param name="jobId">The job's id.</param>
    /// <param name="owner">The signed-in visitor.</param>
    /// <returns>Whether a running job was asked; a finished or unknown one answers <see langword="false"/>.</returns>
    public bool Cancel(PagePath page, string jobId, PageUser? owner) {
        ArgumentNullException.ThrowIfNull(page);

        PageJob? job = Find(jobId, owner);
        if (job is null || job.Snapshot().State is not (PageJobState.Queued or PageJobState.Running)) {
            return false;
        }

        logger.LogInformation("Page {Page} asked job {JobId} to stop", page.Key, job.Id);
        job.Lifetime.Cancel();

        return true;
    }

    /// <summary>Finds one job by id, whoever owns it.</summary>
    /// <param name="jobId">The job's id.</param>
    /// <returns>The job, or <see langword="null"/> when no job has that id.</returns>
    internal PageJob? Find(string jobId) =>
        jobId is not null && _jobs.TryGetValue(jobId, out PageJob? job) ? job : null;

    // A job is found only through its owner, which is what makes another visitor's id an unknown one
    // rather than a refusal that says it exists.
    private PageJob? Find(string jobId, PageUser? owner) {
        if (owner is null) {
            return null;
        }

        return Find(jobId) is { } job && string.Equals(job.Owner.Name, owner.Name, StringComparison.Ordinal)
            ? job
            : null;
    }

    private async Task<PageKind?> KindAsync(PagePath worker, CancellationToken cancellationToken) {
        PageReadHead? head = await readStore.GetHeadAsync(worker.Key, cancellationToken);

        return head is { IsDeleted: false } ? head.TemplateKind : null;
    }

    // A finished job is kept for its retention so a reload can still read its events, then it goes:
    // the queue is the site's memory of work, and a page's own storage is the record of what it did.
    private void Sweep() {
        DateTimeOffset cutoff = clock.GetUtcNow() - options.Retention;
        foreach (KeyValuePair<string, PageJob> entry in _jobs) {
            PageJobInfo info = entry.Value.Snapshot();
            if (info.EndedAt is { } ended && ended < cutoff && _jobs.TryRemove(entry.Key, out PageJob? gone)) {
                gone.Dispose();
            }
        }
    }

    private bool TooLarge(string message) =>
        System.Text.Encoding.UTF8.GetByteCount(message) > options.MaxMessageBytes;

    private static string NewId() => Guid.NewGuid().ToString("N");

    private PageJobStart RefuseStart(PagePath page, string worker, PageJobRefusal refusal, string reason) {
        logger.LogWarning("Page {Page} was refused a job for {Worker}: {Reason}", page.Key, worker, reason);

        return new PageJobStart(null, refusal);
    }

    private PageJobSendResult RefuseSend(PagePath page, string jobId, PageJobRefusal refusal, string reason) {
        logger.LogWarning("Page {Page} was refused a message to job {JobId}: {Reason}", page.Key, jobId, reason);

        return new PageJobSendResult(refusal);
    }
}
