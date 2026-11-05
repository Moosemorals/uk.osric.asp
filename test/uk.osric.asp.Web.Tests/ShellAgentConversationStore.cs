// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Agent;
using uk.osric.asp.Web.Agent;
using uk.osric.asp.Web.Storage;

internal sealed class ShellAgentConversationStore(IReadOnlyList<AgentMessage>? messages = null) : IAgentConversationStore {
    private static AgentConversation Conversation => new() {
        Id = 1,
        Owner = "owner",
        Title = "Test conversation",
        TitleIsGenerated = false,
        ApprovalMode = AgentApprovalMode.RequireApproval.ToString(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public Task<AgentConversation> CreateAsync(string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(Conversation);

    public Task<IReadOnlyList<AgentConversationListItem>> ListAsync(string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentConversationListItem>>([
            new(1, "Test conversation", AgentApprovalMode.RequireApproval.ToString(), DateTimeOffset.UtcNow, null),
        ]);

    public Task<AgentConversation?> GetAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<AgentConversation?>(id == 1 && owner == "owner" ? Conversation : null);

    public Task<bool> RenameAsync(long id, string owner, string title, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<bool> SetApprovalModeAsync(long id, string owner, AgentApprovalMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<bool> DeleteAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == 1 && owner == "owner");

    public Task<AgentRunStartResult> StartRunAsync(long id, string owner, string content, string model, string promptVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgentRunStartResult(AgentRunStartStatus.Started, "run-1", 1));

    public Task<long?> AppendMessageAsync(long conversationId, string runId, AgentMessageInput message, CancellationToken cancellationToken = default) =>
        Task.FromResult<long?>(1);

    public Task<IReadOnlyList<AgentMessage>> GetMessagesAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult(messages ?? []);

    public Task<AgentRun?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<AgentRun?>(null);

    public Task<AgentRun?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<AgentRun?>(null);

    public Task<bool> SetRunStatusAsync(string runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> CreateApprovalAsync(string runId, string toolCallId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<AgentApprovalResolutionResult> ResolveApprovalAsync(long conversationId, string owner, string runId, string toolCallId, string decision, CancellationToken cancellationToken = default) =>
        Task.FromResult(AgentApprovalResolutionResult.Approved);

    public Task<IReadOnlyList<AgentToolApproval>> GetPendingApprovalsAsync(long conversationId, string owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentToolApproval>>([]);

    public Task<int> FailPendingApprovalsAsync(string runId, string failureCode, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<bool> RecordStepAsync(string runId, int toolCallCount, AgentUsage? usage, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
