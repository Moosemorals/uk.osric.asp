// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>The accumulated values from one provider response.</summary>
public sealed record AssistantChatResponse(
    string Content,
    string Reasoning,
    IReadOnlyList<AssistantToolCall> ToolCalls,
    string? FinishReason,
    AssistantUsage? Usage) {

    /// <summary>Whether the provider emitted user-visible or actionable data.</summary>
    public bool HasContent => Content.Length > 0 || Reasoning.Length > 0 || ToolCalls.Count > 0;
}
