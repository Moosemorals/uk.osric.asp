// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Starts and watches the jobs that run a stored worker.</summary>
/// <remarks>
/// <para>
/// A job is one run of a worker: it outlives the request that started it, so it has no response to
/// write and no visitor waiting on it. This is the page's side of it — start one, talk to it, find it
/// again, read what it published and stop it — and it is a capability rather than an
/// <see cref="IContext"/> member, so a page that never wants one never meets it.
/// </para>
/// <para>
/// Nothing here names an owner. The handle is bound to the signed-in visitor who asked, every call is
/// filtered to that visitor's jobs, and an anonymous render is refused a start rather than quietly
/// given somebody else's work. Every refusal is a value.
/// </para>
/// </remarks>
public interface IPageJobs {

    /// <summary>Starts one run of the worker stored at <paramref name="workerKey"/>.</summary>
    /// <param name="workerKey">The canonical key of a stored worker.</param>
    /// <param name="firstMessage">
    /// The first message the worker receives, or <see langword="null"/> when it needs none. Bounded by
    /// <c>Pages:Jobs:MaxMessageBytes</c>.
    /// </param>
    /// <returns>The job's id, or the reason it was refused.</returns>
    public Task<PageJobStart> StartAsync(string workerKey, string? firstMessage);

    /// <summary>Sends a message to a running job.</summary>
    /// <param name="jobId">The id a start or a listing answered with.</param>
    /// <param name="message">The message text or JSON, bounded by <c>Pages:Jobs:MaxMessageBytes</c>.</param>
    /// <returns>Whether it was queued, or the reason it was refused.</returns>
    public Task<PageJobSendResult> SendAsync(string jobId, string message);

    /// <summary>Reads one job, if this visitor owns it.</summary>
    /// <param name="jobId">The id a start or a listing answered with.</param>
    /// <returns>The job, or <see langword="null"/> when it is unknown or another visitor's.</returns>
    public Task<PageJobInfo?> GetAsync(string jobId);

    /// <summary>Lists this visitor's jobs, newest first.</summary>
    /// <returns>The jobs this visitor started, in the order they were started.</returns>
    public Task<IReadOnlyList<PageJobInfo>> ListAsync();

    /// <summary>Reads a job's events after a sequence, waiting briefly for the next one.</summary>
    /// <param name="jobId">The id a start or a listing answered with.</param>
    /// <param name="after">The last sequence the caller has already seen; <c>0</c> reads from the start.</param>
    /// <param name="waitSeconds">
    /// How long to wait for an event that has not arrived yet, limited by <c>Pages:Jobs:MaxWaitSeconds</c>
    /// and by the render's own budget. Omitted or <c>null</c> does not wait.
    /// </param>
    /// <returns>
    /// The events after <paramref name="after"/>, or a reset when the journal no longer holds that
    /// sequence, with the job's state once it has ended.
    /// </returns>
    public Task<PageJobEvents> ReadEventsAsync(string jobId, long after = 0, double? waitSeconds = null);

    /// <summary>Asks a job to stop.</summary>
    /// <param name="jobId">The id a start or a listing answered with.</param>
    /// <returns>Whether the job was there to stop, or was already finished.</returns>
    public Task<bool> CancelAsync(string jobId);
}
