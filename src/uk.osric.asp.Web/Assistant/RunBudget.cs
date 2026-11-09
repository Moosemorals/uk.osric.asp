// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Globalization;

/// <summary>The run budgets a run can run out of, as the run and the page name them.</summary>
/// <remarks>
/// The first two are the budget rows the page draws, so their names are page-facing; the third has no
/// row of its own because it bounds one step rather than the run.
/// </remarks>
internal static class RunBudget {

    /// <summary>The run has taken all the steps its budget allows.</summary>
    public const string Steps = "steps";

    /// <summary>The run has made all the tool calls its budget allows.</summary>
    public const string ToolCalls = "tool-calls";

    /// <summary>One step asked for more tool calls than a single step may make.</summary>
    public const string ToolCallsPerStep = "tool-calls-per-step";

    /// <summary>The status a run takes while it waits for the owner to continue it.</summary>
    public const string Awaiting = "awaiting_continuation";

    /// <summary>The sentence that says which budget ran out, and what continuing buys.</summary>
    /// <param name="budget">One of the budget constants.</param>
    /// <param name="limit">The limit the run reached.</param>
    /// <param name="window">How much a continuation adds.</param>
    /// <remarks>
    /// One sentence, written by the host, so the live card and the reloaded one cannot say different
    /// things about the same pause.
    /// </remarks>
    public static string Sentence(string budget, int limit, int window) => budget switch {
        Steps => $"The run has used all {Format(limit)} of its steps. Continuing gives it another {Format(window)}.",
        ToolCalls => $"The run has used all {Format(limit)} of its tool calls. Continuing gives it another {Format(window)}.",
        _ => $"The run asked for more tool calls in one step than that step's limit allows. Continuing gives it another {Format(window)}.",
    };

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
