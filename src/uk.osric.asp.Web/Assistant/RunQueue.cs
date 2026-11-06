// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>A run waiting for a worker.</summary>
internal sealed record QueuedRun(long ConversationId, string RunId, string Owner);

/// <summary>The bounded queue of runs waiting for a worker.</summary>
internal sealed class RunQueue {
    private readonly Channel<QueuedRun> _queue;
    private readonly int _capacity;
    private int _reserved;

    public RunQueue(AssistantOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        _capacity = options.MaxQueuedRuns;
        _queue = Channel.CreateBounded<QueuedRun>(new BoundedChannelOptions(options.MaxQueuedRuns) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

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

    public void ReleaseReservation() => Interlocked.Decrement(ref _reserved);

    public bool TryEnqueueReserved(QueuedRun run) => _queue.Writer.TryWrite(run);

    public bool TryEnqueue(QueuedRun run) {
        if (!TryReserve()) {
            return false;
        }
        if (TryEnqueueReserved(run)) {
            return true;
        }
        ReleaseReservation();
        return false;
    }

    public async IAsyncEnumerable<QueuedRun> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken) {
        await foreach (QueuedRun run in _queue.Reader.ReadAllAsync(cancellationToken)) {
            ReleaseReservation();
            yield return run;
        }
    }
}
