// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>A message to append to a conversation.</summary>
internal sealed record MessageInput(
    string Role,
    string Content,
    string? Reasoning = null,
    string? ToolCallsJson = null,
    string? ToolCallId = null,
    string? ToolName = null,
    string Status = "complete");
