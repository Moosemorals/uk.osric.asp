// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>A provider message used by the pure context-budgeting rules.</summary>
/// <param name="Role">The chat role, such as <c>system</c>, <c>user</c>, <c>assistant</c> or <c>tool</c>.</param>
/// <param name="Content">The plain-text message content.</param>
/// <param name="ToolCallsJson">Complete assistant tool-call data, when present.</param>
/// <param name="ToolCallId">The tool-call id for a tool result.</param>
/// <param name="ToolName">The tool name for a tool result.</param>
public sealed record ChatMessage(
    string Role,
    string Content,
    string? ToolCallsJson = null,
    string? ToolCallId = null,
    string? ToolName = null);

/// <summary>The result of fitting whole conversation turns into a request-size budget.</summary>
/// <param name="Request">The smallest request that was built: system prompt plus the turns kept.</param>
/// <param name="DroppedMessages">How many transcript messages were omitted.</param>
/// <param name="SentBytes">The size of <paramref name="Request"/> as it would be sent.</param>
/// <param name="Error">A safe reason no request can be built, when the newest turn cannot fit.</param>
public sealed record ContextTrimResult(
    ChatRequest Request,
    int DroppedMessages,
    long SentBytes,
    string? Error) {

    /// <summary>Whether older conversation messages were omitted.</summary>
    public bool IsTrimmed => DroppedMessages > 0;

    /// <summary>A display-safe note to include when the request omitted earlier turns.</summary>
    public string? TrimmingNote => IsTrimmed
        ? "Earlier conversation history was omitted to fit the request budget."
        : null;
}

/// <summary>Fits a request to a byte budget by dropping the oldest whole turns.</summary>
public static class ContextTrimmer {

    /// <summary>Keeps the system prompt and the newest whole turns that fit.</summary>
    /// <param name="request">The full request, whose messages are the transcript to trim.</param>
    /// <param name="maximumBytes">The largest request body the provider may be sent.</param>
    /// <returns>The smallest request that fits, or the newest turn with an error when that cannot.</returns>
    public static ContextTrimResult Trim(ChatRequest request, long maximumBytes) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (request.Messages is null) {
            throw new ArgumentNullException(nameof(request), "The request has no messages.");
        }

        ConversationSplit split = ConversationTurns.Split(request.Messages);
        if (split.Turns.Count == 0) {
            ChatRequest only = request with { Messages = ConversationTurns.Flatten(split.System, []) };
            long bytes = ChatRequestJson.MeasureBytes(only);
            return new ContextTrimResult(only, 0, bytes, bytes <= maximumBytes ? null : TooLong(bytes, maximumBytes));
        }

        List<IReadOnlyList<ChatMessage>> kept = [];
        for (int index = split.Turns.Count - 1; index >= 0; index--) {
            List<IReadOnlyList<ChatMessage>> candidate = [split.Turns[index], .. kept];
            ChatRequest candidateRequest = request with {
                Messages = ConversationTurns.Flatten(split.System, candidate),
            };
            long bytes = ChatRequestJson.MeasureBytes(candidateRequest);
            if (bytes > maximumBytes) {
                // The newest turn is the smallest request there is; once it does not fit, an older turn
                // would only make the request larger, and dropping it is not an option.
                if (kept.Count == 0) {
                    return new ContextTrimResult(candidateRequest, request.Messages.Count - candidateRequest.Messages.Count,
                        bytes, TooLong(bytes, maximumBytes));
                }
                break;
            }

            kept = candidate;
        }

        ChatRequest trimmed = request with { Messages = ConversationTurns.Flatten(split.System, kept) };
        long sentBytes = ChatRequestJson.MeasureBytes(trimmed);
        return new ContextTrimResult(trimmed, request.Messages.Count - trimmed.Messages.Count, sentBytes, null);
    }

    private static string TooLong(long bytes, long maximumBytes) =>
        $"The request needs {bytes} bytes and the budget is {maximumBytes}. "
        + "The system prompt, tool definitions and newest turn alone are too large.";
}
