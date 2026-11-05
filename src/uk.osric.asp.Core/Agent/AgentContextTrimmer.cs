// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Agent;

/// <summary>A provider message used by the pure context-budgeting rules.</summary>
/// <param name="Role">The chat role, such as <c>system</c>, <c>user</c>, <c>assistant</c> or <c>tool</c>.</param>
/// <param name="Content">The plain-text message content.</param>
/// <param name="ToolCallsJson">Complete assistant tool-call data, when present.</param>
/// <param name="ToolCallId">The tool-call id for a tool result.</param>
/// <param name="ToolName">The tool name for a tool result.</param>
public sealed record AgentChatMessage(
    string Role,
    string Content,
    string? ToolCallsJson = null,
    string? ToolCallId = null,
    string? ToolName = null);

/// <summary>The result of fitting complete conversation turns into a character budget.</summary>
/// <param name="Messages">Messages to send, in provider order.</param>
/// <param name="DroppedMessages">How many transcript messages were omitted.</param>
/// <param name="Error">A safe reason no request can be built, when the newest turn cannot fit.</param>
public sealed record AgentContextTrimResult(
    IReadOnlyList<AgentChatMessage> Messages,
    int DroppedMessages,
    string? Error) {

    /// <summary>Whether older conversation messages were omitted.</summary>
    public bool WasTrimmed => DroppedMessages > 0;

    /// <summary>A display-safe note to include when the request omitted earlier turns.</summary>
    public string? TrimmingNote => WasTrimmed
        ? "Earlier conversation history was omitted to fit the context budget."
        : null;
}

/// <summary>Fits a transcript to an approximate character budget without splitting turns.</summary>
public static class AgentContextTrimmer {

    /// <summary>Keeps the system prompt and the newest whole turns that fit.</summary>
    /// <param name="messages">The transcript, with the system prompt first when present.</param>
    /// <param name="maximumChars">The maximum approximate message size.</param>
    /// <returns>The ordered messages, or an error when the newest turn alone cannot fit.</returns>
    public static AgentContextTrimResult Trim(IReadOnlyList<AgentChatMessage> messages, int maximumChars) {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChars);

        int systemCount = messages.Count > 0 && messages[0].Role == "system" ? 1 : 0;
        long systemChars = systemCount == 0 ? 0 : MessageLength(messages[0]);
        if (systemChars > maximumChars) {
            return TooLong();
        }

        List<List<AgentChatMessage>> turns = [];
        foreach (AgentChatMessage message in messages.Skip(systemCount)) {
            if (message.Role == "user" || turns.Count == 0) {
                turns.Add([]);
            }
            turns[^1].Add(message);
        }

        List<AgentChatMessage> kept = [];
        long usedChars = systemChars;
        int keptMessages = systemCount;
        for (int index = turns.Count - 1; index >= 0; index--) {
            List<AgentChatMessage> turn = turns[index];
            long turnChars = turn.Sum(MessageLength);
            if (usedChars + turnChars > maximumChars) {
                if (kept.Count == 0) {
                    return TooLong();
                }
                break;
            }

            kept.InsertRange(0, turn);
            usedChars += turnChars;
            keptMessages += turn.Count;
        }

        if (systemCount == 1) {
            kept.Insert(0, messages[0]);
        }

        return new AgentContextTrimResult(kept, messages.Count - keptMessages, null);

        static AgentContextTrimResult TooLong() => new([], 0,
            "The system prompt and newest user turn exceed the configured context budget.");
    }

    private static int MessageLength(AgentChatMessage message) =>
        message.Content.Length + (message.ToolCallsJson?.Length ?? 0);
}
