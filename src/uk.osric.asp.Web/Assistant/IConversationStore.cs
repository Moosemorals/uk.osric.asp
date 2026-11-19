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
    /// <summary>Appends a message and returns its stored id, or null when the conversation is gone.</summary>
    Task<long?> AppendMessageAsync(long conversationId, string runId, MessageInput message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> GetMessagesAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<Message?> GetMessageAsync(long conversationId, string owner, long messageId, CancellationToken cancellationToken = default);
    Task<Run?> GetActiveRunAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<Run?> GetLastRunAsync(long id, string owner, CancellationToken cancellationToken = default);
    Task<Run?> GetRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default);
    Task<bool> SetRunStatusAsync(string runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken cancellationToken = default);

    /// <summary>Opens the run's one interrupt and moves the run to <c>awaiting_owner</c>.</summary>
    /// <param name="runId">The run that is stopping.</param>
    /// <param name="kind">One of <see cref="OwnerInterruptKind"/>.</param>
    /// <param name="toolCallId">The call the wait belongs to, or null.</param>
    /// <param name="requestJson">What the owner is shown, as <see cref="InterruptPayloads"/> writes it.</param>
    /// <param name="cancellationToken">The run's token.</param>
    /// <returns>Whether the interrupt was opened; false when the run was not running or already had one.</returns>
    Task<bool> OpenInterruptAsync(string runId, string kind, string? toolCallId, string requestJson, CancellationToken cancellationToken = default);

    /// <summary>The run's interrupt, whatever its state, or null.</summary>
    /// <param name="runId">The run whose interrupt is read.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<OwnerInterrupt?> GetInterruptAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>The conversation's pending interrupt, or null when the owner is not being waited on.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="owner">The signed-in owner.</param>
    /// <param name="cancellationToken">The request's token.</param>
    Task<OwnerInterrupt?> GetPendingInterruptAsync(long conversationId, string owner, CancellationToken cancellationToken = default);

    /// <summary>Every question this conversation's runs asked and its owner answered, oldest first.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="owner">The signed-in owner.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <remarks>
    /// The transcript's answered card is rendered from these rows rather than the tool result, because
    /// a long result is cut at the tool cap and an answer cut in half is worse than no card.
    /// </remarks>
    Task<IReadOnlyList<OwnerInterrupt>> GetAnsweredQuestionsAsync(
        long conversationId,
        string owner,
        CancellationToken cancellationToken = default);

    /// <summary>Records the owner's resolution and moves the run back to <c>running</c>.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="owner">The signed-in owner.</param>
    /// <param name="runId">The run whose interrupt is resolved.</param>
    /// <param name="kind">The kind the caller read, checked against the record.</param>
    /// <param name="toolCallId">The call the caller read, checked against the record.</param>
    /// <param name="resolutionJson">What the owner gave, as <see cref="InterruptPayloads"/> writes it.</param>
    /// <param name="cancellationToken">The request's token.</param>
    Task<InterruptResolutionResult> ResolveInterruptAsync(
        long conversationId,
        string owner,
        string runId,
        string kind,
        string? toolCallId,
        string resolutionJson,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a run's continuation interrupt and widens the budget it ran out of.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="owner">The signed-in owner.</param>
    /// <param name="runId">The paused run.</param>
    /// <param name="cancellationToken">The request's token.</param>
    /// <returns>Whether a pending continuation was resolved; false when the run is not waiting on one.</returns>
    Task<bool> ContinueRunAsync(long conversationId, string owner, string runId, CancellationToken cancellationToken = default);

    /// <summary>Fails every interrupt a run still has pending.</summary>
    /// <param name="runId">The run that stopped.</param>
    /// <param name="failureCode">Why it stopped.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    Task<int> FailPendingInterruptAsync(string runId, string failureCode, CancellationToken cancellationToken = default);
    Task<bool> RecordStepAsync(
        string runId,
        long requestBytes,
        long estimatedTokens,
        int maxTokens,
        TokenUsage? usage,
        CancellationToken cancellationToken = default);
    /// <summary>Counts one tool call against the run when it is attempted, which is what the cap spends.</summary>
    Task<bool> RecordToolCallAsync(string runId, CancellationToken cancellationToken = default);
    Task<int> RecoverInterruptedRunsAsync(CancellationToken cancellationToken = default);
}
