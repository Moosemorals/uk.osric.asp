// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

using uk.osric.asp.Core.Agent;
using uk.osric.asp.Web.Storage;

internal sealed record AgentConversationListItem(
    long Id,
    string Title,
    string ApprovalMode,
    DateTimeOffset UpdatedAt,
    string? ActiveRunId);

internal enum AgentRunStartStatus {
    Started,
    NotFound,
    AlreadyActive,
}

internal enum AgentApprovalResolutionResult {
    Approved,
    Denied,
    NotFound,
    Conflict,
}

internal sealed record AgentRunStartResult(AgentRunStartStatus Status, string? RunId, long? UserMessageSequence);

internal sealed record AgentMessageInput(
    string Role,
    string Content,
    string? Reasoning = null,
    string? ToolCallsJson = null,
    string? ToolCallId = null,
    string? ToolName = null,
    string Status = "complete");

internal interface IAgentConversationStore {
    Task<AgentConversation> CreateAsync(string owner, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentConversationListItem>> ListAsync(string owner, CancellationToken cancellationToken = default);
    Task<AgentConversation?> GetAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<bool> RenameAsync(long id, string owner, string title, CancellationToken cancellationToken = default);
    Task<bool> SetApprovalModeAsync(long id, string owner, AgentApprovalMode mode, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<AgentRunStartResult> StartRunAsync(long id, string owner, string content, string model, string promptVersion, CancellationToken cancellationToken = default);
    Task<long?> AppendMessageAsync(long conversationId, string runId, AgentMessageInput message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentMessage>> GetMessagesAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<AgentRun?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<AgentRun?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default);
    Task<bool> SetRunStatusAsync(string runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task<bool> CreateApprovalAsync(string runId, string toolCallId, CancellationToken cancellationToken = default);
    Task<AgentApprovalResolutionResult> ResolveApprovalAsync(long conversationId, string owner, string runId, string toolCallId, string decision, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentToolApproval>> GetPendingApprovalsAsync(long conversationId, string owner, CancellationToken cancellationToken = default);
    Task<int> FailPendingApprovalsAsync(string runId, string failureCode, CancellationToken cancellationToken = default);
    Task<bool> RecordStepAsync(string runId, int toolCallCount, AgentUsage? usage, CancellationToken cancellationToken = default);
    Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default);
}
