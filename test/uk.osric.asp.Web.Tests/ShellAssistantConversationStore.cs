// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Storage;

internal sealed class ShellAssistantConversationStore(IReadOnlyList<AssistantMessage>? messages = null) : IAssistantConversationStore {
    private static AssistantConversation Conversation => new() {
        Id = 1,
        Owner = "owner",
        Title = "Test conversation",
        TitleIsGenerated = false,
        ApprovalMode = AssistantApprovalMode.RequireApproval.ToString(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public Task<AssistantConversation> CreateAsync(string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(Conversation);

    public Task<IReadOnlyList<AssistantConversationListItem>> ListAsync(string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssistantConversationListItem>>([
            new(1, "Test conversation", AssistantApprovalMode.RequireApproval.ToString(), DateTimeOffset.UtcNow, null),
        ]);

    public Task<AssistantConversation?> GetAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<AssistantConversation?>(id == 1 && owner == "owner" ? Conversation : null);

    public Task<bool> RenameAsync(long id, string owner, string title, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<bool> SetApprovalModeAsync(long id, string owner, AssistantApprovalMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<bool> DeleteAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<AssistantRunStartResult> StartRunAsync(long id, string owner, string content, string model, string promptVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AssistantRunStartResult(AssistantRunStartStatus.Started, "run-1", 1));

    public Task<long?> AppendMessageAsync(long conversationId, string runId, AssistantMessageInput message, CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(1);

    public Task<IReadOnlyList<AssistantMessage>> GetMessagesAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(messages ?? []);

    public Task<AssistantRun?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<AssistantRun?>(null);

    public Task<AssistantRun?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<AssistantRun?>(null);

    public Task<bool> SetRunStatusAsync(string runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> CreateApprovalAsync(string runId, string toolCallId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<AssistantApprovalResolutionResult> ResolveApprovalAsync(long conversationId, string owner, string runId, string toolCallId, string decision, CancellationToken cancellationToken = default) =>
        Task.FromResult(AssistantApprovalResolutionResult.Approved);

    public Task<IReadOnlyList<AssistantToolApproval>> GetPendingApprovalsAsync(long conversationId, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssistantToolApproval>>([]);

    public Task<int> FailPendingApprovalsAsync(string runId, string failureCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<bool> RecordStepAsync(string runId, int toolCallCount, AssistantUsage? usage, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
