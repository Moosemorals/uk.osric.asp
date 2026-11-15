// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using System.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>One job's published events: a bounded ring a viewer can replay, and wait on.</summary>
/// <remarks>
/// <para>
/// Events are progress, not a record: the ring keeps the most recent ones and drops the oldest past
/// <c>MaxEvents</c>, <c>MaxEventBytes</c> or <c>RetentionMinutes</c>, because a worker that runs for a
/// working day publishes more than any page could read. What must survive belongs in the starting
/// page's storage.
/// </para>
/// <para>
/// A viewer that asks for a sequence the ring no longer holds is told so rather than quietly handed a
/// gap, and a wait is answered by a signal the publisher swaps under the lock, so an event published
/// while a viewer is arming is not missed.
/// </para>
/// </remarks>
internal sealed class PageJobJournal(PageJobOptions options, TimeProvider clock) {

    private readonly Queue<Entry> _events = [];
    private readonly Lock _sync = new();
    private TaskCompletionSource _signal = NewSignal();
    private int _bytes;
    private long _next = 1;

    /// <summary>Publishes one event, dropping it when it is larger than the journal allows.</summary>
    /// <param name="type">What the event is.</param>
    /// <param name="dataJson">Its text or JSON.</param>
    /// <returns><see langword="false"/> when the event was too large to keep and was dropped.</returns>
    public bool Publish(string type, string dataJson) {
        int bytes = Encoding.UTF8.GetByteCount(type) + Encoding.UTF8.GetByteCount(dataJson);
        if (bytes > options.MaxEventBytes) {
            return false;
        }

        TaskCompletionSource signal;
        lock (_sync) {
            Trim();

            long sequence = _next++;
            _events.Enqueue(new Entry(new PageJobEvent(sequence, type, dataJson), bytes, clock.GetUtcNow()));
            _bytes += bytes;
            while (_events.Count > options.MaxEvents || _bytes > options.MaxEventBytes) {
                EvictOldest();
            }

            signal = _signal;
            _signal = NewSignal();
        }

        _ = signal.TrySetResult();

        return true;
    }

    /// <summary>Wakes every waiter, because the job ended and no more events are coming.</summary>
    public void SignalEnd() {
        TaskCompletionSource signal;
        lock (_sync) {
            signal = _signal;
            _signal = NewSignal();
        }

        _ = signal.TrySetResult();
    }

    /// <summary>Waits until an event is published or the job ends.</summary>
    /// <param name="wait">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when there is something new to read.</returns>
    public async Task WaitAsync(TimeSpan wait, CancellationToken cancellationToken) {
        Task signal;
        lock (_sync) {
            signal = _signal.Task;
        }

        try {
            await signal.WaitAsync(wait, cancellationToken);
        } catch (TimeoutException) {
            // Nothing arrived within the wait, which is an answer rather than a failure.
        }
    }

    /// <summary>Reads every event after a sequence.</summary>
    /// <param name="after">The last sequence the caller has seen; <c>0</c> reads from the start.</param>
    public Snapshot Read(long after) {
        lock (_sync) {
            Trim();

            long first = _events.Count > 0 ? _events.Peek().Event.Sequence : _next;
            if (after < first - 1) {
                // The caller missed events this ring no longer holds, so it starts again from what is
                // here rather than from a gap it can never fill.
                List<PageJobEvent> held = [.. _events.Select(entry => entry.Event)];

                return new Snapshot(held, held.Count > 0 ? held[^1].Sequence : first - 1, Reset: true);
            }

            List<PageJobEvent> events = [];
            foreach (Entry entry in _events) {
                if (entry.Event.Sequence > after) {
                    events.Add(entry.Event);
                }
            }

            return new Snapshot(events, events.Count > 0 ? events[^1].Sequence : after, Reset: false);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Trim() {
        DateTimeOffset cutoff = clock.GetUtcNow() - options.Retention;
        while (_events.Count > 0 && _events.Peek().At < cutoff) {
            EvictOldest();
        }
    }

    private void EvictOldest() {
        Entry oldest = _events.Dequeue();
        _bytes -= oldest.Bytes;
    }

    /// <summary>What one read of the ring answered.</summary>
    /// <param name="Events">The events after the caller's sequence.</param>
    /// <param name="Next">The sequence to pass back next.</param>
    /// <param name="Reset">Whether the caller's sequence had been dropped.</param>
    internal sealed record Snapshot(IReadOnlyList<PageJobEvent> Events, long Next, bool Reset);

    private sealed record Entry(PageJobEvent Event, int Bytes, DateTimeOffset At);
}
