// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

internal sealed class ShellConversationStore(
    IReadOnlyList<Message>? messages = null,
    Run? lastRun = null) : IConversationStore {
    private static Conversation Conversation => new() {
        Id = 1,
        Owner = "owner",
        Title = "Test conversation",
        HasGeneratedTitle = false,
        ApprovalMode = ApprovalMode.RequireApproval.ToString(),
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

    public Task<Run?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<Run?>(null);

    public Task<Run?> GetLastRunAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(lastRun);

    public Task<Run?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Run?>(null);

    public Task<bool> SetRunStatusAsync(string runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> CreateApprovalAsync(string runId, string toolCallId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<ApprovalResolutionResult> ResolveApprovalAsync(long conversationId, string owner, string runId, string toolCallId, string decision, CancellationToken cancellationToken = default) =>
        Task.FromResult(ApprovalResolutionResult.Approved);

    public Task<IReadOnlyList<ToolApproval>> GetPendingApprovalsAsync(long conversationId, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ToolApproval>>([]);

    public Task<int> FailPendingApprovalsAsync(string runId, string failureCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<bool> RecordStepAsync(string runId, int toolCallCount, TokenUsage? usage, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
