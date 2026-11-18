// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Collections.Concurrent;

/// <summary>Wakes the worker of a run whose owner has resolved its interrupt.</summary>
/// <remarks>
/// The run's worker holds the loop's state while it waits, so the signal only has to wake it: the
/// resolution is read from the store, which is where the endpoint put it. A run waits on one thing at a
/// time, so the run id is the whole key and a second wait for the same run is a defect rather than a
/// separate wait to track.
/// </remarks>
internal sealed class OwnerInterruptCoordinator {
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _pending = new(StringComparer.Ordinal);

    /// <summary>Waits for the owner's resolution of this run's interrupt.</summary>
    /// <param name="runId">The run whose interrupt is pending.</param>
    /// <param name="cancellationToken">The run's token.</param>
    public async Task WaitAsync(string runId, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(runId, signal)) {
            throw new InvalidOperationException("An interrupt wait already exists for this run.");
        }

        try {
            await signal.Task.WaitAsync(cancellationToken);
        } finally {
            _pending.TryRemove(runId, out _);
        }
    }

    /// <summary>Wakes the run's worker, if it is waiting.</summary>
    /// <param name="runId">The run whose interrupt was resolved.</param>
    /// <returns>Whether a waiter was woken.</returns>
    public bool Signal(string runId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return _pending.TryGetValue(runId, out TaskCompletionSource? signal) && signal.TrySetResult();
    }
}
