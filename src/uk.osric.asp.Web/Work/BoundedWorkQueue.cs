// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>A bounded queue of background work, with a slot taken before the work is queued.</summary>
/// <remarks>
/// A reservation is taken before the work is written, so a queue that is full refuses the request
/// rather than growing without bound, and the read that starts the work is what releases the slot.
/// The channel's own bound is the same number, so it is a backstop rather than the limit.
/// </remarks>
/// <typeparam name="T">The work item.</typeparam>
internal abstract class BoundedWorkQueue<T>(int capacity) {

    private readonly Channel<T> _queue = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity) {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    private int _reserved;

    /// <summary>Takes one of the queue's slots.</summary>
    /// <returns><see langword="false"/> when every slot is taken.</returns>
    public bool TryReserve() {
        while (true) {
            int current = Volatile.Read(ref _reserved);
            if (current >= capacity) {
                return false;
            }

            if (Interlocked.CompareExchange(ref _reserved, current + 1, current) == current) {
                return true;
            }
        }
    }

    /// <summary>Returns a reservation that was not used.</summary>
    public void ReleaseReservation() => Interlocked.Decrement(ref _reserved);

    /// <summary>Queues work whose reservation is already held.</summary>
    /// <param name="item">The work to queue.</param>
    /// <returns><see langword="false"/> when the channel would not accept it.</returns>
    public bool TryEnqueueReserved(T item) => _queue.Writer.TryWrite(item);

    /// <summary>Reserves a slot and queues work.</summary>
    /// <param name="item">The work to queue.</param>
    /// <returns><see langword="false"/> when the queue is full.</returns>
    public bool TryEnqueue(T item) {
        if (!TryReserve()) {
            return false;
        }

        if (TryEnqueueReserved(item)) {
            return true;
        }

        ReleaseReservation();
        return false;
    }

    /// <summary>Reads queued work until the host stops, releasing each slot as its work starts.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken) {
        await foreach (T item in _queue.Reader.ReadAllAsync(cancellationToken)) {
            ReleaseReservation();
            yield return item;
        }
    }
}
