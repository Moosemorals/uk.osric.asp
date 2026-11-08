// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text.Json;

/// <summary>What a conversation has spent of each assistant budget the page shows.</summary>
/// <param name="ContextChars">The characters the whole transcript needs, counted as the trimmer counts them.</param>
/// <param name="Steps">The steps the current or most recent run has taken.</param>
/// <param name="ToolCalls">The tool calls that run has made.</param>
/// <param name="LongestArguments">The longest serialized tool-call arguments in the transcript.</param>
/// <param name="LongestResult">The longest stored tool result in the transcript.</param>
/// <param name="CompletionTokens">The most recent completion tokens the provider reported.</param>
public sealed record BudgetSnapshot(
    long ContextChars,
    int Steps,
    int ToolCalls,
    int LongestArguments,
    int LongestResult,
    int CompletionTokens) {

    /// <summary>The figures before anything has been spent.</summary>
    public static BudgetSnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0);

    /// <summary>Measures a transcript, so the page and the run stream cannot disagree.</summary>
    /// <param name="messages">The transcript to measure, with the system prompt first when present.</param>
    /// <param name="steps">The steps the current or most recent run has taken.</param>
    /// <param name="toolCalls">The tool calls that run has made.</param>
    /// <param name="completionTokens">The most recent completion tokens, or null when the provider has not reported any.</param>
    /// <returns>The figures, with the counters carried through unchanged.</returns>
    public static BudgetSnapshot Measure(
        IReadOnlyList<ChatMessage> messages,
        int steps,
        int toolCalls,
        int? completionTokens) {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentOutOfRangeException.ThrowIfNegative(steps);
        ArgumentOutOfRangeException.ThrowIfNegative(toolCalls);

        int longestArguments = 0;
        int longestResult = 0;
        foreach (ChatMessage message in messages) {
            longestArguments = Math.Max(longestArguments, LongestArgumentsIn(message.ToolCallsJson));
            if (message.Role == "tool") {
                longestResult = Math.Max(longestResult, message.Content.Length);
            }
        }

        return new BudgetSnapshot(
            ContextTrimmer.MeasureChars(messages),
            steps,
            toolCalls,
            longestArguments,
            longestResult,
            completionTokens ?? 0);
    }

    private static int LongestArgumentsIn(string? toolCallsJson) {
        if (toolCallsJson is null) {
            return 0;
        }

        using JsonDocument document = JsonDocument.Parse(toolCallsJson);
        int longest = 0;
        foreach (JsonElement call in document.RootElement.EnumerateArray()) {
            if (call.TryGetProperty("function", out JsonElement function)
                && function.TryGetProperty("arguments", out JsonElement arguments)
                && arguments.ValueKind == JsonValueKind.String) {
                longest = Math.Max(longest, (arguments.GetString() ?? string.Empty).Length);
            }
        }
        return longest;
    }
}
