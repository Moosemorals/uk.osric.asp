// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>What a run has spent, accumulated over its steps.</summary>
/// <param name="PromptTokens">Every step's prompt tokens added together.</param>
/// <param name="CompletionTokens">Every step's completion tokens added together.</param>
/// <param name="CachedTokens">Every step's cache-hit prompt tokens added together.</param>
/// <param name="ReasoningTokens">Every step's reasoning completion tokens added together.</param>
/// <param name="OutputTokens">The most recent step's completion tokens, which the output budget counts.</param>
public sealed record RunUsage(
    int PromptTokens,
    int CompletionTokens,
    int CachedTokens,
    int ReasoningTokens,
    int OutputTokens) {

    /// <summary>The figures before anything has been spent.</summary>
    public static RunUsage Empty { get; } = new(0, 0, 0, 0, 0);

    /// <summary>The prompt and completion tokens together; cached and reasoning are already inside them.</summary>
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>Adds one step's report, keeping the same rule the store uses.</summary>
    /// <param name="usage">The step's report, or null when the provider did not send one.</param>
    /// <returns>The totals, with the output figure replaced only when this step reported one.</returns>
    public RunUsage Add(TokenUsage? usage) => usage is null ? this : this with {
        PromptTokens = PromptTokens + (usage.PromptTokens ?? 0),
        CompletionTokens = CompletionTokens + (usage.CompletionTokens ?? 0),
        CachedTokens = CachedTokens + (usage.CachedTokens ?? 0),
        ReasoningTokens = ReasoningTokens + (usage.ReasoningTokens ?? 0),
        OutputTokens = usage.CompletionTokens ?? OutputTokens,
    };
}
