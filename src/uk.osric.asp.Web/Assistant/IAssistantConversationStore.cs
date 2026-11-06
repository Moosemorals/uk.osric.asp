// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>Stores conversations, their messages, runs and tool approvals.</summary>
internal interface IAssistantConversationStore {
    Task<AssistantConversation> CreateAsync(string owner, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssistantConversationListItem>> ListAsync(string owner, CancellationToken cancellationToken = default);
    Task<AssistantConversation?> GetAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<bool> RenameAsync(long id, string owner, string title, CancellationToken cancellationToken = default);
    Task<bool> SetApprovalModeAsync(long id, string owner, AssistantApprovalMode mode, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<AssistantRunStartResult> StartRunAsync(long id, string owner, string content, string model, string promptVersion, CancellationToken cancellationToken = default);
    Task<long?> AppendMessageAsync(long conversationId, string runId, AssistantMessageInput message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssistantMessage>> GetMessagesAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<AssistantRun?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<AssistantRun?> GetLastRunAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<AssistantRun?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default);
    Task<bool> SetRunStatusAsync(string runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task<bool> CreateApprovalAsync(string runId, string toolCallId, CancellationToken cancellationToken = default);
    Task<AssistantApprovalResolutionResult> ResolveApprovalAsync(long conversationId, string owner, string runId, string toolCallId, string decision, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssistantToolApproval>> GetPendingApprovalsAsync(long conversationId, string owner, CancellationToken cancellationToken = default);
    Task<int> FailPendingApprovalsAsync(string runId, string failureCode, CancellationToken cancellationToken = default);
    Task<bool> RecordStepAsync(string runId, int toolCallCount, AssistantUsage? usage, CancellationToken cancellationToken = default);
    Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default);
}
