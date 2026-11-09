// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>Stores conversations, their messages, runs and tool approvals.</summary>
internal interface IConversationStore {
    Task<Conversation> CreateAsync(string owner, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationListItem>> ListAsync(string owner, CancellationToken cancellationToken = default);
    Task<Conversation?> GetAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<bool> RenameAsync(long id, string owner, string title, CancellationToken cancellationToken = default);
    Task<bool> SetApprovalModeAsync(long id, string owner, ApprovalMode mode, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<RunStartResult> StartRunAsync(long id, string owner, string content, string model, string promptVersion, CancellationToken cancellationToken = default);
    Task<long?> AppendMessageAsync(long conversationId, string runId, MessageInput message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> GetMessagesAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<Message?> GetMessageAsync(long conversationId, string owner, long messageId, CancellationToken cancellationToken = default);
    Task<Run?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<Run?> GetLastRunAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<Run?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default);
    Task<bool> SetRunStatusAsync(string runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task<bool> PauseRunAsync(string runId, string budget, CancellationToken cancellationToken = default);
    Task<bool> ContinueRunAsync(long conversationId, string owner, string runId, int stepWindow, int toolCallWindow, CancellationToken cancellationToken = default);
    Task<bool> CreateApprovalAsync(string runId, string toolCallId, CancellationToken cancellationToken = default);
    Task<ApprovalResolutionResult> ResolveApprovalAsync(long conversationId, string owner, string runId, string toolCallId, string decision, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ToolApproval>> GetPendingApprovalsAsync(long conversationId, string owner, CancellationToken cancellationToken = default);
    Task<int> FailPendingApprovalsAsync(string runId, string failureCode, CancellationToken cancellationToken = default);
    Task<bool> RecordStepAsync(string runId, int toolCallCount, long requestBytes, TokenUsage? usage, CancellationToken cancellationToken = default);
    Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default);
}
