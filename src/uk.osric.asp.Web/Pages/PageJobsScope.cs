// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Work;

/// <summary>One layer's <see cref="IPageJobs"/>, bound to the credential that started it.</summary>
/// <remarks>
/// <para>
/// It is offered to every page layer and never to a layout: a layout that started work would start it
/// once per page it wraps, and the page that can read the events is the one that asked for the job.
/// Nobody is offered it by <see cref="IContext"/> — a page that never wants a job never meets one.
/// </para>
/// <para>
/// Every job a layer starts carries the authority the render captured, so a child started by a worker
/// dies with the same sign-in. A start asks that authority first, because a page's render and a job's
/// run both hold one and one path is cheaper than a flag.
/// </para>
/// <para>
/// A <c>HEAD</c> is offered the reads and not the writes, because a HEAD runs the page to produce its
/// representation and starting work is a side effect the visitor never asked for. The refusal is a
/// value rather than an exception, so a page can branch on it.
/// </para>
/// </remarks>
internal sealed class PageJobsScope(
    PagePath path,
    bool isHead,
    PageJobAuthority? authority,
    PageJobs jobs,
    PageJobOptions options,
    Action? touched = null) : RenderBoundCapability, IPageJobs {

    /// <inheritdoc/>
    public async Task<PageJobStart> StartAsync(string workerKey, string? firstMessage) {
        if (isHead) {
            return new PageJobStart(null, PageJobRefusal.ReadOnly);
        }

        ThrowIfEnded();

        if (authority is null) {
            return await jobs.StartAsync(path, workerKey, firstMessage, null, Cancellation());
        }

        touched?.Invoke();
        if (!await authority.IsLiveAsync(Cancellation())) {
            return new PageJobStart(null, PageJobRefusal.Revoked);
        }

        return await jobs.StartAsync(path, workerKey, firstMessage, authority, Cancellation());
    }

    /// <inheritdoc/>
    public Task<PageJobSendResult> SendAsync(string jobId, string message) {
        if (isHead) {
            return Task.FromResult(new PageJobSendResult(PageJobRefusal.ReadOnly));
        }

        ThrowIfEnded();

        return Task.FromResult(jobs.Send(path, jobId, message, authority?.Owner));
    }

    /// <inheritdoc/>
    public Task<PageJobInfo?> GetAsync(string jobId) {
        ThrowIfEnded();

        return Task.FromResult(jobs.Get(jobId, authority?.Owner));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PageJobInfo>> ListAsync() {
        ThrowIfEnded();

        return Task.FromResult(jobs.List(authority?.Owner));
    }

    /// <inheritdoc/>
    public Task<PageJobEvents> ReadEventsAsync(string jobId, long after = 0, double? waitSeconds = null) {
        ThrowIfEnded();

        TimeSpan wait = waitSeconds switch {
            null => TimeSpan.Zero,
            <= 0 => TimeSpan.Zero,
            var seconds => TimeSpan.FromSeconds(Math.Min(seconds.Value, options.MaxWaitSeconds)),
        };

        return jobs.ReadEventsAsync(jobId, after, wait, authority?.Owner, Cancellation());
    }

    /// <inheritdoc/>
    public Task<bool> CancelAsync(string jobId) {
        ThrowIfEnded();

        return Task.FromResult(!isHead && jobs.Cancel(path, jobId, authority?.Owner));
    }

    private CancellationToken Cancellation() => Deadline;

    private void ThrowIfEnded() {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot use its jobs.");
        }
    }
}
