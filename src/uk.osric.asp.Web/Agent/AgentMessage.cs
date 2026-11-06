// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Agent;

/// <summary>One stored message of a conversation, including its tool calls and their results.</summary>
internal sealed class AgentMessage {
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public required string RunId { get; set; }
    public long Sequence { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public string? Reasoning { get; set; }
    public string? ToolCallsJson { get; set; }
    public string? ToolCallId { get; set; }
    public string? ToolName { get; set; }
    public string Status { get; set; } = "complete";
    public DateTimeOffset CreatedAt { get; set; }
}
