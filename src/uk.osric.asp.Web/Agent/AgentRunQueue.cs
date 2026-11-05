// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

internal sealed record AgentQueuedRun(long ConversationId, string RunId, string Owner);

internal sealed class AgentRunQueue {
    private readonly Channel<AgentQueuedRun> _queue;
    private readonly int _capacity;
    private int _reserved;

    public AgentRunQueue(AgentOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        _capacity = options.MaxQueuedRuns;
        _queue = Channel.CreateBounded<AgentQueuedRun>(new BoundedChannelOptions(options.MaxQueuedRuns) {
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

    public bool TryEnqueueReserved(AgentQueuedRun run) => _queue.Writer.TryWrite(run);

    public bool TryEnqueue(AgentQueuedRun run) {
        if (!TryReserve()) {
            return false;
        }
        if (TryEnqueueReserved(run)) {
            return true;
        }
        ReleaseReservation();
        return false;
    }

    public async IAsyncEnumerable<AgentQueuedRun> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken) {
        await foreach (AgentQueuedRun run in _queue.Reader.ReadAllAsync(cancellationToken)) {
            ReleaseReservation();
            yield return run;
        }
    }
}
