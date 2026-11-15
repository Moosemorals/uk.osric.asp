// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Work;

/// <summary>One layer's <see cref="IPageJobs"/>, bound to that render's visitor.</summary>
/// <remarks>
/// <para>
/// It is offered to every page layer and never to a layout: a layout that started work would start it
/// once per page it wraps, and the page that can read the events is the one that asked for the job.
/// Nobody is offered it by <see cref="IContext"/> — a page that never wants a job never meets one.
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
    PageUser? user,
    PageJobs jobs,
    PageJobOptions options) : RenderBoundCapability, IPageJobs {

    /// <inheritdoc/>
    public Task<PageJobStart> StartAsync(string workerKey, string? firstMessage) {
        if (isHead) {
            return Task.FromResult(new PageJobStart(null, PageJobRefusal.ReadOnly));
        }

        ThrowIfEnded();

        return jobs.StartAsync(path, workerKey, firstMessage, user, Cancellation());
    }

    /// <inheritdoc/>
    public Task<PageJobSendResult> SendAsync(string jobId, string message) {
        if (isHead) {
            return Task.FromResult(new PageJobSendResult(PageJobRefusal.ReadOnly));
        }

        ThrowIfEnded();

        return Task.FromResult(jobs.Send(path, jobId, message, user));
    }

    /// <inheritdoc/>
    public Task<PageJobInfo?> GetAsync(string jobId) {
        ThrowIfEnded();

        return Task.FromResult(jobs.Get(jobId, user));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PageJobInfo>> ListAsync() {
        ThrowIfEnded();

        return Task.FromResult(jobs.List(user));
    }

    /// <inheritdoc/>
    public Task<PageJobEvents> ReadEventsAsync(string jobId, long after = 0, double? waitSeconds = null) {
        ThrowIfEnded();

        TimeSpan wait = waitSeconds switch {
            null => TimeSpan.Zero,
            <= 0 => TimeSpan.Zero,
            var seconds => TimeSpan.FromSeconds(Math.Min(seconds.Value, options.MaxWaitSeconds)),
        };

        return jobs.ReadEventsAsync(jobId, after, wait, user, Cancellation());
    }

    /// <inheritdoc/>
    public Task<bool> CancelAsync(string jobId) {
        ThrowIfEnded();

        return Task.FromResult(!isHead && jobs.Cancel(path, jobId, user));
    }

    private CancellationToken Cancellation() => Deadline;

    private void ThrowIfEnded() {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot use its jobs.");
        }
    }
}
