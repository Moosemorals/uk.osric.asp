// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using System.Threading.Channels;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

/// <summary>One job in memory: its identity, its credential, its messages and its journal.</summary>
/// <remarks>
/// A job does not survive a restart, so nothing here is stored: the starting page's own state is the
/// record, and a page that finds its state says running while <see cref="IPageJobs.GetAsync"/> answers
/// nothing knows the host restarted. The record is kept after the job ends until it is swept, so a
/// viewer that reloads can still read what the job published.
/// <para>
/// The authority is the credential the starting render presented, which the job acts with until it
/// ends. <see cref="Owner"/> is its label, which is what a viewer sees and what every job call is
/// filtered by; the credential is what its writes are proved against.
/// </para>
/// </remarks>
internal sealed class PageJob : IDisposable {

    private readonly TimeProvider _clock;
    private readonly Channel<string> _messages;
    private readonly Lock _sync = new();
    private DateTimeOffset? _endedAt;
    private string? _failure;
    private PageJobState _state = PageJobState.Queued;

    /// <summary>Creates a job that is waiting for a runner.</summary>
    /// <param name="id">The job's id.</param>
    /// <param name="worker">The worker's canonical key.</param>
    /// <param name="page">The starting page's canonical key.</param>
    /// <param name="authority">The credential the starting render presented.</param>
    /// <param name="input">The first message, or <see langword="null"/> when there was none.</param>
    /// <param name="options">The job's bounds.</param>
    /// <param name="clock">Where the times come from.</param>
    public PageJob(
        string id,
        PagePath worker,
        PagePath page,
        PageJobAuthority authority,
        string? input,
        PageJobOptions options,
        TimeProvider clock) {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        Id = id;
        Worker = worker;
        Page = page;
        Authority = authority;
        Input = input;
        StartedAt = clock.GetUtcNow();
        _clock = clock;
        Journal = new PageJobJournal(options, clock);
        Lifetime = new JobLifetime(options);
        _messages = Channel.CreateBounded<string>(new BoundedChannelOptions(options.MaxPendingMessages) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>The job's id.</summary>
    public string Id { get; }

    /// <summary>The worker's canonical key.</summary>
    public PagePath Worker { get; }

    /// <summary>The starting page's canonical key.</summary>
    public PagePath Page { get; }

    /// <summary>The credential the job acts with, borrowed from the render that started it.</summary>
    public PageJobAuthority Authority { get; }

    /// <summary>The account the job runs for, which is the label every job call is filtered by.</summary>
    public PageUser Owner => Authority.Owner;

    /// <summary>The first message, or <see langword="null"/>.</summary>
    public string? Input { get; }

    /// <summary>When the job was accepted.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>The events the worker has published.</summary>
    public PageJobJournal Journal { get; }

    /// <summary>The job's own clocks.</summary>
    public JobLifetime Lifetime { get; }

    /// <summary>Reads the job as its owner sees it.</summary>
    public PageJobInfo Snapshot() {
        lock (_sync) {
            return new PageJobInfo(
                Id, Worker.Key, Page.Key, _state, StartedAt, _endedAt, _failure);
        }
    }

    /// <summary>Marks the job as running, once a runner has it.</summary>
    public void MarkRunning() {
        lock (_sync) {
            if (_state is PageJobState.Queued) {
                _state = PageJobState.Running;
            }
        }
    }

    /// <summary>Queues one message for the worker.</summary>
    /// <param name="message">The message.</param>
    /// <returns><see cref="PageJobRefusal.None"/> when it was queued, or why it was not.</returns>
    public PageJobRefusal TrySend(string message) {
        lock (_sync) {
            if (_state is not (PageJobState.Queued or PageJobState.Running)) {
                return PageJobRefusal.Ended;
            }

            return _messages.Writer.TryWrite(message) ? PageJobRefusal.None : PageJobRefusal.TooManyMessages;
        }
    }

    /// <summary>Waits for the next message.</summary>
    /// <param name="wait">How long to wait, or <see langword="null"/> to wait until the job ends.</param>
    /// <param name="aborted">Cancels the wait when the job must stop.</param>
    /// <returns>The message, or <see langword="null"/> when the wait elapsed or the job ended.</returns>
    /// <remarks>
    /// The wait pauses the silence clock: a worker waiting for its owner has not gone quiet, and the
    /// ceiling is what bounds it.
    /// </remarks>
    public async Task<string?> ReceiveAsync(TimeSpan? wait, CancellationToken aborted) {
        Lifetime.Pause();

        try {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(aborted);
            if (wait is { } waitSpan) {
                linked.CancelAfter(waitSpan);
            }

            try {
                return await _messages.Reader.ReadAsync(linked.Token);
            } catch (OperationCanceledException) {
                return null;
            } catch (ChannelClosedException) {
                return null;
            }
        } finally {
            Lifetime.Resume();
        }
    }

    /// <summary>Ends the job and wakes everyone waiting on it.</summary>
    /// <param name="state">How it ended.</param>
    /// <param name="failure">Why it failed, or <see langword="null"/> when it did not.</param>
    public void End(PageJobState state, string? failure) {
        lock (_sync) {
            if (_endedAt is not null) {
                return;
            }

            _state = state;
            _failure = failure;
            _endedAt = _clock.GetUtcNow();
        }

        _ = _messages.Writer.TryComplete();
        Journal.SignalEnd();
        Lifetime.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose() => Lifetime.Dispose();
}
