// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Agent;

/// <summary>The accumulated values from one provider response.</summary>
public sealed record AgentChatResponse(
    string Content,
    string Reasoning,
    IReadOnlyList<AgentToolCall> ToolCalls,
    string? FinishReason,
    AgentUsage? Usage) {

    /// <summary>Whether the provider emitted user-visible or actionable data.</summary>
    public bool HasContent => Content.Length > 0 || Reasoning.Length > 0 || ToolCalls.Count > 0;
}
