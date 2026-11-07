// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>The bounded queue of imports waiting for the worker.</summary>
/// <remarks>
/// The shape is <c>RunQueue</c>'s: a reservation is taken before the work is queued, so a queue that
/// is full refuses the request rather than growing without bound. With one editor this is a backstop,
/// not a feature — the worker runs one job at a time because there is one compile slot.
/// </remarks>
internal sealed class ImportQueue {
    private readonly Channel<string> _queue;
    private readonly int _capacity;
    private int _reserved;

    /// <summary>Creates the queue.</summary>
    /// <param name="options">The archive options, whose staged-import count bounds the queue.</param>
    public ImportQueue(ArchiveOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        _capacity = Math.Max(1, options.MaxStagedImports);
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(_capacity) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Takes one of the queue's slots.</summary>
    /// <returns><see langword="false"/> when every slot is taken.</returns>
    public bool TryReserve() {
        while (true) {
            int current = Volatile.Read(ref _reserved);
            if (current >= _capacity) {
                return false;
            }

            if (Interlocked.CompareExchange(ref _reserved, current + 1, current) == current) {
                return true;
            }
        }
    }

    /// <summary>Returns a reservation that was not used.</summary>
    public void ReleaseReservation() => Interlocked.Decrement(ref _reserved);

    /// <summary>Queues a stage whose reservation is already held.</summary>
    /// <param name="stageId">The stage to import.</param>
    public bool TryEnqueueReserved(string stageId) => _queue.Writer.TryWrite(stageId);

    /// <summary>Reserves a slot and queues a stage.</summary>
    /// <param name="stageId">The stage to import.</param>
    /// <returns><see langword="false"/> when the queue is full.</returns>
    public bool TryEnqueue(string stageId) {
        if (!TryReserve()) {
            return false;
        }

        if (TryEnqueueReserved(stageId)) {
            return true;
        }

        ReleaseReservation();
        return false;
    }

    /// <summary>Reads queued stages until the host stops.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async IAsyncEnumerable<string> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken) {
        await foreach (string stageId in _queue.Reader.ReadAllAsync(cancellationToken)) {
            ReleaseReservation();
            yield return stageId;
        }
    }
}
