// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a running worker is told about, and can do in, the job it is running.</summary>
/// <remarks>
/// <para>
/// It is deliberately not an <see cref="IContext"/>: a worker has no response to shape and no visitor
/// waiting on it, so it is offered no way to write, flush, redirect or set a status. What it has is
/// who it is running for, what it was sent, a way to ask for a capability, a way to wait for the next
/// message and a way to publish what it has done.
/// </para>
/// <para>
/// A job is started by a signed-in visitor, so <see cref="User"/> is always the owner: a worker acts
/// as that account for as long as the job runs. A worker that wants a connection, a store or the page
/// catalog asks for it through <see cref="Capabilities"/> exactly as a page does; the host decides
/// which are there, and a worker is offered no renderer and no layout. Its writer is bound to the
/// page that started the job and to the credential that did: when that credential is revoked, the
/// write answers <see cref="PageWriteOutcome.Revoked"/> and stores nothing, and the worker decides
/// whether to stop, retry or publish what it has.
/// </para>
/// </remarks>
public interface IWorkerContext {

    /// <summary>This job's id, which the starting page used to start it.</summary>
    public string Id { get; }

    /// <summary>The canonical key of the page that started the job.</summary>
    public string Path { get; }

    /// <summary>The canonical key of the worker source that is running.</summary>
    public string Worker { get; }

    /// <summary>The visitor the job runs for.</summary>
    public PageUser User { get; }

    /// <summary>The first message the job was started with, or <see langword="null"/> when it had none.</summary>
    public string? Input { get; }

    /// <summary>
    /// Cancelled when the job must stop: a page asked, the host is stopping, or the job outran its
    /// silence or duration limit. A worker that ignores it is ended where it next awaits.
    /// </summary>
    public CancellationToken Aborted { get; }

    /// <summary>What this job offers, which is fewer things than a page's render does.</summary>
    public IPageCapabilities Capabilities { get; }

    /// <summary>The job's data: the starting page's, the starter's, and the worker's own namespace.</summary>
    /// <remarks>
    /// A job has no session and no browser, so its <see cref="IPageStores.Visitor"/> has only
    /// <see cref="StoreLifetime.Person"/>; <see cref="StoreLifetime.Session"/> and
    /// <see cref="StoreLifetime.Browser"/> are refused.
    /// </remarks>
    public IPageStores Stores { get; }

    /// <summary>Writes to the job's log.</summary>
    /// <param name="message">The message.</param>
    public void Log(string message);

    /// <summary>Writes a warning to the job's log.</summary>
    /// <param name="message">The message.</param>
    public void LogWarning(string message);

    /// <summary>Writes an error to the job's log.</summary>
    /// <param name="message">The message.</param>
    public void LogError(string message);

    /// <summary>Waits for the next message sent to this job.</summary>
    /// <param name="waitSeconds">
    /// How long to wait, capped by <c>Pages:Jobs:MaxWaitSeconds</c>. Omitted or <see langword="null"/>
    /// waits until a message arrives or the job ends.
    /// </param>
    /// <returns>
    /// The message, or <see langword="null"/> when the wait elapsed or the job can no longer receive —
    /// <see cref="Aborted"/> tells the two apart. A wait is not silence: it pauses the job's silence
    /// clock, so a worker waiting on its owner is bounded by the job's ceiling instead.
    /// </returns>
    public Task<string?> ReceiveAsync(double? waitSeconds = null);

    /// <summary>Publishes one event to this job's journal.</summary>
    /// <param name="type">What the event is, such as <c>delta</c> or <c>done</c>.</param>
    /// <param name="dataJson">The event's text or JSON, bounded by <c>Pages:Jobs:MaxEventBytes</c>.</param>
    /// <remarks>
    /// A viewer reads it with <see cref="IPageJobs.ReadEventsAsync"/> and waits for the next one; the
    /// journal keeps the most recent events and drops the oldest past its bounds, so an event is a
    /// progress report rather than a record. What must survive belongs in the starting page's storage.
    /// </remarks>
    public void Publish(string type, string dataJson);
}
