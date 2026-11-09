// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Collections.Concurrent;

/// <summary>Pairs a run paused on a budget with the owner's decision to carry on.</summary>
/// <remarks>
/// The run's worker is holding the loop's state while it waits, so the decision only has to wake it:
/// the new limits are read from the store, which is where the endpoint put them.
/// </remarks>
internal sealed class RunContinuationCoordinator {
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _pending = new(StringComparer.Ordinal);

    public async Task WaitAsync(string runId, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(runId, signal)) {
            throw new InvalidOperationException("A continuation wait already exists for this run.");
        }

        try {
            await signal.Task.WaitAsync(cancellationToken);
        } finally {
            _pending.TryRemove(runId, out _);
        }
    }

    public bool Signal(string runId) =>
        _pending.TryGetValue(runId, out TaskCompletionSource? signal) && signal.TrySetResult();
}
