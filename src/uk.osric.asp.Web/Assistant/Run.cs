// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>One stored run of a conversation: its status, model, usage and any error.</summary>
internal sealed class Run {
    public required string Id { get; set; }
    public long ConversationId { get; set; }
    public required string Status { get; set; }
    public required string Model { get; set; }
    public required string ApprovalMode { get; set; }
    public required string PromptVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int StepCount { get; set; }
    public int ToolCallCount { get; set; }
    /// <summary>Every step's prompt tokens added together.</summary>
    public int? PromptTokens { get; set; }
    /// <summary>Every step's completion tokens added together.</summary>
    public int? CompletionTokens { get; set; }
    /// <summary>Every step's cache-hit prompt tokens added together.</summary>
    public int? CachedTokens { get; set; }
    /// <summary>Every step's reasoning tokens added together.</summary>
    public int? ReasoningTokens { get; set; }
    /// <summary>The most recent step's completion tokens, which the output budget counts.</summary>
    public int? OutputTokens { get; set; }
    /// <summary>The UTF-8 bytes of the last request this run sent, for calibrating the token estimate.</summary>
    public long? RequestBytes { get; set; }
    /// <summary>The last request's provider-reported prompt tokens, for calibrating the token estimate.</summary>
    public int? LastPromptTokens { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The budget the run is paused on, or null when it is not paused.</summary>
    /// <remarks>One of <see cref="RunBudget.Steps"/>, <see cref="RunBudget.ToolCalls"/> or
    /// <see cref="RunBudget.ToolCallsPerStep"/> while <see cref="Status"/> is
    /// <c>awaiting_continuation</c>.</remarks>
    public string? PausedBudget { get; set; }

    /// <summary>The run's step budget, or null while it is the configured one.</summary>
    /// <remarks>A continuation adds one window to the budget it ran out of, so this is what the page
    /// shows and what the processor resumes with.</remarks>
    public int? StepLimit { get; set; }

    /// <summary>The run's tool-call budget, or null while it is the configured one.</summary>
    public int? ToolCallLimit { get; set; }

    /// <summary>The stored totals as the value the page and the run publish.</summary>
    public uk.osric.asp.Core.Assistant.RunUsage ToUsage() => new(
        PromptTokens ?? 0,
        CompletionTokens ?? 0,
        CachedTokens ?? 0,
        ReasoningTokens ?? 0,
        OutputTokens ?? 0);
}
