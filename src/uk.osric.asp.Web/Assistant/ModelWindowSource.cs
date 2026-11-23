// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

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
        // refuse the small one a test uses to make a limit bite.
        Volatile.Write(ref source._budget, budget);
        Volatile.Write(ref source._read, ModelWindowRead.Ok(budget.ContextWindow, budget.MaximumOutputTokens));
        return source;
    }
}
