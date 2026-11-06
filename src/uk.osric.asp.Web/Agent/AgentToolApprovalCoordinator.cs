// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using System.Collections.Concurrent;

/// <summary>Pairs a run waiting on an approval with the decision the browser submits.</summary>
internal sealed class AgentToolApprovalCoordinator {
    private readonly ConcurrentDictionary<(string RunId, string ToolCallId), TaskCompletionSource<AgentApprovalResolutionResult>> _pending = [];

    public async Task<AgentApprovalResolutionResult> WaitAsync(
        string runId,
        string toolCallId,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        TaskCompletionSource<AgentApprovalResolutionResult> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    public bool Signal(string runId, string toolCallId, AgentApprovalResolutionResult decision) {
        if (decision is not (AgentApprovalResolutionResult.Approved or AgentApprovalResolutionResult.Denied)) {
            return false;
        }

        return _pending.TryGetValue((runId, toolCallId), out TaskCompletionSource<AgentApprovalResolutionResult>? signal)
            && signal.TrySetResult(decision);
    }
}
