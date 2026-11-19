// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

internal sealed class ShellConversationStore(
    IReadOnlyList<Message>? messages = null,
    Run? lastRun = null,
    Run? activeRun = null,
    OwnerInterrupt? pendingInterrupt = null,
    ApprovalMode approvalMode = ApprovalMode.RequireApproval,
    IReadOnlyList<OwnerInterrupt>? answeredQuestions = null) : IConversationStore {
    private Conversation Conversation => new() {
        Id = 1,
        Owner = "owner",
        Title = "Test conversation",
        HasGeneratedTitle = false,
        ApprovalMode = approvalMode.ToString(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public Task<Conversation> CreateAsync(string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(Conversation);

    public Task<IReadOnlyList<ConversationListItem>> ListAsync(string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConversationListItem>>([
            new(1, "Test conversation", ApprovalMode.RequireApproval.ToString(), DateTimeOffset.UtcNow, null),
        ]);

    public Task<Conversation?> GetAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner" ? Conversation : null);

    public Task<bool> RenameAsync(long id, string owner, string title, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<bool> SetApprovalModeAsync(long id, string owner, ApprovalMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<bool> DeleteAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<RunStartResult> StartRunAsync(long id, string owner, string content, string model, string promptVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RunStartResult(RunStartStatus.Started, "run-1", 1));

    public Task<long?> AppendMessageAsync(long conversationId, string runId, MessageInput message, CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(1);

    public Task<IReadOnlyList<Message>> GetMessagesAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(messages ?? []);

    public Task<Message?> GetMessageAsync(long conversationId, string owner, long messageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(conversationId == 1 && owner == "owner"
            ? (messages ?? []).FirstOrDefault(message => message.Id == messageId)
            : null);

    public Task<Run?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(activeRun);

    public Task<Run?> GetLastRunAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(lastRun);

    public Task<Run?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Run?>(null);

    public Task<bool> SetRunStatusAsync(string runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> OpenInterruptAsync(string runId, string kind, string? toolCallId, string requestJson, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<OwnerInterrupt?> GetInterruptAsync(string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<OwnerInterrupt?>(null);

    public Task<OwnerInterrupt?> GetPendingInterruptAsync(long conversationId, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(conversationId == 1 && owner == "owner" ? pendingInterrupt : null);

    public Task<IReadOnlyList<OwnerInterrupt>> GetAnsweredQuestionsAsync(
        long conversationId,
        string owner,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OwnerInterrupt>>(
            conversationId == 1 && owner == "owner" ? answeredQuestions ?? [] : []);

    public Task<InterruptResolutionResult> ResolveInterruptAsync(
        long conversationId,
        string owner,
        string runId,
        string kind,
        string? toolCallId,
        string resolutionJson,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InterruptResolutionResult.Resolved);

    public Task<bool> ContinueRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<int> FailPendingInterruptAsync(string runId, string failureCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<bool> RecordStepAsync(
        string runId,
        long requestBytes,
        long estimatedTokens,
        int maxTokens,
        TokenUsage? usage,
        CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> RecordToolCallAsync(string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
