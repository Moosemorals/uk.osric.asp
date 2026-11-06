// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

/// <summary>One stored run of a conversation: its status, model, usage and any error.</summary>
internal sealed class AgentRun {
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
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
