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

/// <summary>The result of fitting whole conversation turns into a token budget.</summary>
/// <param name="Request">The smallest request that was built: system prompt plus the turns kept.</param>
/// <param name="DroppedMessages">How many transcript messages were omitted.</param>
/// <param name="EstimatedTokens">The estimated size of <paramref name="Request"/> as it would be sent.</param>
/// <param name="RequestBytes">The exact UTF-8 bytes of <paramref name="Request"/>, for calibration.</param>
/// <param name="Error">A safe reason no request can be built, when the newest turn cannot fit.</param>
public sealed record ContextTrimResult(
    ChatRequest Request,
    int DroppedMessages,
    long EstimatedTokens,
    long RequestBytes,
    string? Error) {

    /// <summary>Whether older conversation messages were omitted.</summary>
    public bool IsTrimmed => DroppedMessages > 0;

    /// <summary>A display-safe note to include when the request omitted earlier turns.</summary>
    public string? TrimmingNote => IsTrimmed
        ? "Earlier conversation history was omitted to fit the request budget."
        : null;
}

/// <summary>Fits a request to a token budget by dropping the oldest whole turns.</summary>
/// <remarks>
/// Each message is measured once and the turns are summed from those figures, so building a candidate
/// is arithmetic rather than a serialization. The whole-turn rule is unchanged: a turn is never split
/// from its tool calls or results, and the newest turn is the smallest request there is.
/// </remarks>
public static class ContextTrimmer {

    /// <summary>Keeps the system prompt and the newest whole turns that fit.</summary>
    /// <param name="request">The full request, whose messages are the transcript to trim.</param>
    /// <param name="maximumTokens">The largest estimated prompt the provider may be sent.</param>
    /// <param name="estimator">The estimator the budget is measured with.</param>
    /// <returns>The smallest request that fits, or the newest turn with an error when that cannot.</returns>
    public static ContextTrimResult Trim(ChatRequest request, long maximumTokens, TokenEstimator estimator) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(estimator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTokens);
        if (request.Messages is null) {
            throw new ArgumentNullException(nameof(request), "The request has no messages.");
        }

        PromptSizes sizes = PromptSizes.Measure(request);
        ConversationSplit split = ConversationTurns.Split(request.Messages);
        if (split.Turns.Count == 0) {
            ChatRequest only = request with { Messages = ConversationTurns.Flatten(split.System, []) };
            long bytes = sizes.Of(only.Messages);
            long tokens = estimator.Estimate(bytes);
            return new ContextTrimResult(only, 0, tokens, bytes, tokens <= maximumTokens ? null : TooLong(tokens, bytes, maximumTokens));
        }

        // The system prompt and the commas between messages are fixed whatever is kept, so a candidate
        // is the kept turns' sizes added to that fixed part.
        long systemBytes = split.System is null ? 0 : sizes.SizeOf(split.System);
        List<IReadOnlyList<ChatMessage>> kept = [];
        int keptCount = 0;
        long keptBytes = 0;
        for (int index = split.Turns.Count - 1; index >= 0; index--) {
            long turnBytes = 0;
            foreach (ChatMessage message in split.Turns[index]) {
                turnBytes += sizes.SizeOf(message);
            }

            long bytes = sizes.Body(
                systemBytes + keptBytes + turnBytes,
                (split.System is null ? 0 : 1) + keptCount + split.Turns[index].Count);
            long tokens = estimator.Estimate(bytes);
            if (tokens > maximumTokens) {
                // The newest turn is the smallest request there is; once it does not fit, an older turn
                // would only make the request larger, and dropping it is not an option.
                if (kept.Count == 0) {
                    ChatRequest newest = request with {
                        Messages = ConversationTurns.Flatten(split.System, [split.Turns[index]]),
                    };
                    return new ContextTrimResult(
                        newest,
                        request.Messages.Count - newest.Messages.Count,
                        tokens,
                        bytes,
                        TooLong(tokens, bytes, maximumTokens));
                }
                break;
            }

            kept = [split.Turns[index], .. kept];
            keptCount += split.Turns[index].Count;
            keptBytes += turnBytes;
        }

        ChatRequest trimmed = request with { Messages = ConversationTurns.Flatten(split.System, kept) };
        long sentBytes = sizes.Of(trimmed.Messages);
        long sentTokens = estimator.Estimate(sentBytes);
        return new ContextTrimResult(
            trimmed,
            request.Messages.Count - trimmed.Messages.Count,
            sentTokens,
            sentBytes,
            null);
    }

    private static string TooLong(long tokens, long bytes, long maximumTokens) =>
        $"The request needs about {tokens} tokens ({bytes} bytes) and the budget is {maximumTokens} tokens. "
        + "The system prompt, tool definitions and newest turn alone are too large.";
}
