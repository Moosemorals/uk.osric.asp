// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Collections.Concurrent;

/// <summary>Pairs a run waiting on an approval with the decision the browser submits.</summary>
internal sealed class AssistantToolApprovalCoordinator {
    private readonly ConcurrentDictionary<(string RunId, string ToolCallId), TaskCompletionSource<AssistantApprovalResolutionResult>> _pending = [];

    public async Task<AssistantApprovalResolutionResult> WaitAsync(
        string runId,
        string toolCallId,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        TaskCompletionSource<AssistantApprovalResolutionResult> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        (string RunId, string ToolCallId) key = (runId, toolCallId);
        if (!_pending.TryAdd(key, signal)) {
            throw new InvalidOperationException("An approval wait already exists for this tool call.");
        }

        try {
            return await signal.Task.WaitAsync(cancellationToken);
        } finally {
            _pending.TryRemove(key, out _);
        }
    }

    public bool Signal(string runId, string toolCallId, AssistantApprovalResolutionResult decision) {
        if (decision is not (AssistantApprovalResolutionResult.Approved or AssistantApprovalResolutionResult.Denied)) {
            return false;
        }

        return _pending.TryGetValue((runId, toolCallId), out TaskCompletionSource<AssistantApprovalResolutionResult>? signal)
            && signal.TrySetResult(decision);
    }
}
