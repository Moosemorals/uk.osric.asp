// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text.Json;

/// <summary>What a conversation has spent of each assistant budget the page shows.</summary>
/// <param name="ContextTokens">The estimated tokens of the provider request the plan would send.</param>
/// <param name="Steps">The steps the current or most recent run has taken.</param>
/// <param name="ToolCalls">The tool calls that run has made.</param>
/// <param name="LongestArguments">The longest serialized tool-call arguments in the transcript.</param>
/// <param name="LongestResult">The longest stored tool result in the transcript.</param>
/// <param name="OutputTokens">The most recent completion tokens the provider reported.</param>
public sealed record BudgetSnapshot(
    long ContextTokens,
    int Steps,
    int ToolCalls,
    int LongestArguments,
    int LongestResult,
    int OutputTokens) {

    /// <summary>The figures before anything has been spent.</summary>
    public static BudgetSnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0);

    /// <summary>Measures a plan against the transcript it was built from.</summary>
    /// <param name="plan">The request plan whose <see cref="ContextPlan.EstimatedTokens"/> the row shows.</param>
    /// <param name="stored">The complete stored transcript, which the argument and result rows still measure.</param>
    /// <param name="steps">The steps the current or most recent run has taken.</param>
    /// <param name="toolCalls">The tool calls that run has made.</param>
    /// <param name="outputTokens">The most recent completion tokens, or null when the provider has not reported any.</param>
    /// <returns>The figures, with the counters carried through unchanged.</returns>
    /// <remarks>
    /// Masking changes the request, not the cap a stored call or result had to satisfy, so the longest
    /// argument and result rows read the stored transcript and keep showing the real stored values.
    /// </remarks>
    public static BudgetSnapshot Measure(
        ContextPlan plan,
        IReadOnlyList<ChatMessage> stored,
        int steps,
        int toolCalls,
        int? outputTokens) {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentOutOfRangeException.ThrowIfNegative(steps);
        ArgumentOutOfRangeException.ThrowIfNegative(toolCalls);

        int longestArguments = 0;
        int longestResult = 0;
        foreach (ChatMessage message in stored) {
            longestArguments = Math.Max(longestArguments, LongestArgumentsIn(message.ToolCallsJson));
            if (message.Role == "tool") {
                longestResult = Math.Max(longestResult, message.Content.Length);
            }
        }

        return new BudgetSnapshot(
            plan.EstimatedTokens,
            steps,
            toolCalls,
            longestArguments,
            longestResult,
            outputTokens ?? 0);
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
