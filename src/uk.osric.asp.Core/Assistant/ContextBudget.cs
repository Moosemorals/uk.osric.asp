// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Globalization;

/// <summary>The one content budget: the model's reported window and the reply reserved from it.</summary>
/// <remarks>
/// The provider reports the window and the largest reply the model accepts; the operator chooses only
/// the floor under a reply, so the prompt's share is the window less that floor. Every other content
/// limit is a fraction of the prompt budget rather than a number standing beside it, so no single
/// payload can fill the budget the planner is protecting (<c>assistant-one-budget-decides-what-fits</c>).
/// The fractions are invariants here rather than configuration: a value that could raise one to the
/// whole budget would switch the guard off.
/// </remarks>
public sealed record ContextBudget {

    /// <summary>The conservative bytes one token costs, which is the densest content the spike measured.</summary>
    public const double BytesPerToken = 1.6;

    /// <summary>The least prompt a window must leave room for; a smaller one is a configuration mistake.</summary>
    public const long MinimumPromptTokens = 16_384;

    private const int ToolResultFraction = 8;

    private const int MemoryFraction = 32;

    private ContextBudget(long contextWindow, int maximumOutputTokens, int minimumOutputTokens) {
        ContextWindow = contextWindow;
        MaximumOutputTokens = maximumOutputTokens;
        MinimumOutputTokens = minimumOutputTokens;
    }

    /// <summary>The window the provider reported, counting both the prompt and the reply.</summary>
    public long ContextWindow { get; }

    /// <summary>The largest <c>max_tokens</c> the provider accepts for this model.</summary>
    public int MaximumOutputTokens { get; }

    /// <summary>The floor under a reply, which is the only part of the split an operator chooses.</summary>
    public int MinimumOutputTokens { get; }

    /// <summary>The prompt's share of the window: everything except the reply's floor.</summary>
    public long PromptBudget => ContextWindow - MinimumOutputTokens;

    /// <summary>The most bytes one tool result may carry, an eighth of the prompt budget.</summary>
    public long ToolResultBytes => BytesOf(PromptBudget / ToolResultFraction);

    /// <summary>The most bytes the owner's message may carry, an eighth of the prompt budget.</summary>
    public long MessageBytes => ToolResultBytes;

    /// <summary>The most bytes a run's memory block may cost, a thirty-second of the prompt budget.</summary>
    public long MemoryBytesPerRun => BytesOf(PromptBudget / MemoryFraction);

    /// <summary>Resolves a reported window and the configured floor into the budget a run is given.</summary>
    /// <param name="contextWindow">The window the provider reported, in tokens.</param>
    /// <param name="maximumOutputTokens">The largest reply the provider accepts, in tokens.</param>
    /// <param name="minimumOutputTokens">The configured floor under a reply, in tokens.</param>
    /// <returns>The budget, with the prompt's share and its fractions resolved.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A figure is not positive, or the floor leaves less than <see cref="MinimumPromptTokens"/> of the
    /// window for the prompt.
    /// </exception>
    public static ContextBudget Create(long contextWindow, int maximumOutputTokens, int minimumOutputTokens) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextWindow);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumOutputTokens);
        ArgumentOutOfRangeException.ThrowIfLessThan(contextWindow - minimumOutputTokens, MinimumPromptTokens,
            $"The reply floor ({minimumOutputTokens.ToString(CultureInfo.InvariantCulture)}) leaves less than the "
            + $"{MinimumPromptTokens.ToString(CultureInfo.InvariantCulture)} tokens of the reported window "
            + $"{contextWindow.ToString(CultureInfo.InvariantCulture)} for the prompt.");

        return new ContextBudget(contextWindow, maximumOutputTokens, minimumOutputTokens);
    }

    /// <summary>Builds a budget from a prompt share, for a test that is about the arithmetic.</summary>
    /// <param name="promptTokens">The share of the window the prompt may spend.</param>
    /// <param name="maximumOutputTokens">The largest reply the provider accepts.</param>
    /// <returns>A budget whose prompt share is exactly <paramref name="promptTokens"/>.</returns>
    internal static ContextBudget ForPromptShare(long promptTokens, int maximumOutputTokens = 1_000) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(promptTokens);

        return new ContextBudget(promptTokens + 1, maximumOutputTokens, 1);
    }

    /// <summary>The <c>max_tokens</c> a request estimated at <paramref name="estimatedPrompt"/> is sent with.</summary>
    /// <param name="estimatedPrompt">The prompt's estimated size, including the estimate's margin.</param>
    /// <returns>What the window leaves, limited to the published maximum and floored at the configured minimum.</returns>
    /// <remarks>
    /// The estimate carries the margin, so a low estimate shortens the reply's allowance by the same
    /// amount: the prompt and the reply together stay inside the window rather than needing a haircut.
    /// </remarks>
    public int MaxTokens(long estimatedPrompt) {
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedPrompt);

        long available = ContextWindow - estimatedPrompt;
        if (available < MinimumOutputTokens) {
            return MinimumOutputTokens;
        }
        return available >= MaximumOutputTokens ? MaximumOutputTokens : (int)available;
    }

    /// <summary>The error a step's estimate may carry before it has spent the margin.</summary>
    /// <returns>The margin in tokens, which is the prompt budget times the estimator's margin over one.</returns>
    public long EstimateMargin => (long)(PromptBudget * (TokenEstimator.DeltaMargin - 1));

    private static long BytesOf(long tokens) => (long)(tokens * BytesPerToken);
}
