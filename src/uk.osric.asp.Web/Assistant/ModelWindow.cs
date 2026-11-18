// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>Why the provider has, or has not, told this host what the configured model's limits are.</summary>
internal enum ModelWindowReason {
    /// <summary>The provider answered with the model's window and output limit.</summary>
    Ok,

    /// <summary>The provider answered, and the configured model is not in its list.</summary>
    ModelMissing,

    /// <summary>The provider answered, and the entry omits the window or the output limit.</summary>
    Incomplete,

    /// <summary>The provider did not answer in time.</summary>
    Timeout,

    /// <summary>The provider could not be reached.</summary>
    Unreachable,

    /// <summary>The provider answered with an error status.</summary>
    Error,

    /// <summary>The provider's answer was not the model list.</summary>
    Invalid,

    /// <summary>Nothing has been read yet.</summary>
    Unknown,
}

/// <summary>What the provider's model list says about the configured model.</summary>
/// <param name="ContextWindow">The window the model accepts, in tokens, when it was reported.</param>
/// <param name="MaximumOutputTokens">The largest reply the model accepts, in tokens, when it was reported.</param>
/// <param name="Reason">Whether the figures are there, and why not when they are not.</param>
internal sealed record ModelWindowRead(long? ContextWindow, int? MaximumOutputTokens, ModelWindowReason Reason) {

    /// <summary>The state before anything has been read.</summary>
    public static ModelWindowRead Unknown { get; } = new(null, null, ModelWindowReason.Unknown);

    /// <summary>Whether both figures were reported.</summary>
    public bool IsAvailable => Reason == ModelWindowReason.Ok && ContextWindow is not null && MaximumOutputTokens is not null;

    /// <summary>A read that found both figures.</summary>
    internal static ModelWindowRead Ok(long contextWindow, int maximumOutputTokens) =>
        new(contextWindow, maximumOutputTokens, ModelWindowReason.Ok);

    /// <summary>A read that produced no window, with the reason.</summary>
    internal static ModelWindowRead Failed(ModelWindowReason reason) => new(null, null, reason);

    /// <summary>Whether this answer names the same figures as <paramref name="other"/>.</summary>
    /// <param name="other">The read to compare with.</param>
    /// <returns>True when both carry the same window and output limit.</returns>
    public bool SameAs(ModelWindowRead other) {
        ArgumentNullException.ThrowIfNull(other);
        return ContextWindow == other.ContextWindow && MaximumOutputTokens == other.MaximumOutputTokens;
    }
}

/// <summary>The model window the last read found, and the content budget it resolves to.</summary>
/// <remarks>
/// One immutable value for the process, so a step never waits on a read. The budget is resolved when
/// the read lands rather than on every request; a run that has no budget refuses rather than guessing
/// (<c>assistant-the-budget-is-window-minus-reply</c>).
/// </remarks>
internal sealed class ModelWindowSource {

    private ModelWindowRead _read = ModelWindowRead.Unknown;
    private ContextBudget? _budget;

    /// <summary>The last read.</summary>
    public ModelWindowRead Read => Volatile.Read(ref _read);

    /// <summary>The content budget the last read supports, or null when there is no window.</summary>
    public ContextBudget? Budget => Volatile.Read(ref _budget);

    /// <summary>Records a read, resolving the budget it supports.</summary>
    /// <param name="read">What the provider answered.</param>
    /// <param name="minimumOutputTokens">The configured floor under a reply.</param>
    /// <exception cref="ArgumentOutOfRangeException">The floor leaves too little of the window for the prompt.</exception>
    public void Record(ModelWindowRead read, int minimumOutputTokens) {
        ArgumentNullException.ThrowIfNull(read);

        ContextBudget? budget = read is { IsAvailable: true, ContextWindow: { } window, MaximumOutputTokens: { } output }
            ? ContextBudget.Create(window, output, minimumOutputTokens)
            : null;
        Volatile.Write(ref _budget, budget);
        Volatile.Write(ref _read, read);
    }

    /// <summary>A source fixed to one budget, for a test that is not about the provider.</summary>
    /// <param name="budget">The budget every read answers with.</param>
    public static ModelWindowSource Fixed(ContextBudget budget) {
        ArgumentNullException.ThrowIfNull(budget);

        ModelWindowSource source = new();
        // Set directly rather than through Record, because Record re-derives the budget and would
        // refuse the small one a test uses to make a cap bite.
        Volatile.Write(ref source._budget, budget);
        Volatile.Write(ref source._read, ModelWindowRead.Ok(budget.ContextWindow, budget.MaximumOutputTokens));
        return source;
    }
}
