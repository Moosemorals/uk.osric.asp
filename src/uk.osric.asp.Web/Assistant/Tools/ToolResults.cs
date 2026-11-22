// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using System.Globalization;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;

/// <summary>Writes the tool result envelope, so no tool can bypass the one budget.</summary>
/// <param name="budget">The content budget one tool result may spend.</param>
internal sealed class ToolResults(ContextBudget budget) {

    // The cap is what a tool's answer may cost; the envelope around it — the flag, the note, the
    // summary and JSON escaping — is not an answer and is allowed this much more. A tool that cut its
    // own answer at the cap therefore keeps its shape, and only a tool that ignored the cap is cut here.
    private const int EnvelopeChars = 4_096;

    /// <summary>A result carrying data.</summary>
    /// <param name="data">What the tool produced.</param>
    /// <param name="summary">One line for the transcript.</param>
    public ToolExecutionResult Success(object data, string summary) =>
        Bounded(new { ok = true, data, error = (string?)null }, summary);

    /// <summary>A refusal, or a call that could not be completed.</summary>
    /// <param name="error">The refusal.</param>
    /// <param name="summary">One line for the transcript.</param>
    public ToolExecutionResult Failure(string error, string summary) =>
        Bounded(new { ok = false, data = (object?)null, error }, summary, success: false);

    /// <summary>A refusal written without a budget, for a call that cannot run at all.</summary>
    /// <param name="error">The refusal.</param>
    /// <param name="summary">One line for the transcript.</param>
    /// <returns>The envelope a refusal carries.</returns>
    /// <remarks>A refusal is a sentence this host wrote, so there is nothing here for a cap to cut.</remarks>
    public static ToolExecutionResult Refusal(string error, string summary) =>
        new(false, JsonSerializer.Serialize(new { ok = false, data = (object?)null, error }, JsonSerialization.Options), summary);

    /// <summary>A result, truncated at its cap so the head is kept and the cut is named.</summary>
    /// <param name="envelope">The envelope to serialize.</param>
    /// <param name="summary">One line for the transcript.</param>
    /// <param name="success">Whether the call did what it was asked.</param>
    /// <remarks>
    /// A refusal would lose the information and the fact that there was information, so an oversized
    /// result is cut instead: the head travels, the envelope says <c>truncated</c>, and a note says
    /// where the cut was. Refusals stay for what they are for — an invalid argument, a stale revision,
    /// a path outside the writable prefix, a missing page.
    /// </remarks>
    private ToolExecutionResult Bounded(object envelope, string summary, bool success = true) {
        string json = JsonSerializer.Serialize(envelope, JsonSerialization.Options);
        int cap = (int)Math.Min(budget.ToolResultChars + EnvelopeChars, int.MaxValue);
        if (json.Length <= cap) {
            return new ToolExecutionResult(success, json, summary);
        }

        // The head is what the model can still read; the note is what tells it the rest exists. The
        // envelope around the head costs a fixed handful of characters, so the cut is the cap less
        // that, narrowed once if the note's own digits made the envelope longer than expected.
        string note = $"The tool result was truncated at {cap.ToString(CultureInfo.InvariantCulture)} characters.";
        string head = json;
        string truncated = string.Empty;
        for (int attempt = 0; attempt < 4; attempt++) {
            truncated = JsonSerializer.Serialize(
                new { ok = success, data = head, truncated = true, note }, JsonSerialization.Options);
            if (truncated.Length <= cap) {
                break;
            }
            head = json[..Math.Max(0, head.Length - (truncated.Length - cap) - 8)];
        }

        return new ToolExecutionResult(success, truncated, summary);
    }
}
