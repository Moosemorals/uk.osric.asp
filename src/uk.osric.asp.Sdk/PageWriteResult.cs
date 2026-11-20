// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One line of a commit's report: what became of one write.</summary>
/// <remarks>
/// The commit answers one row per operation, in the order the page staged them, and the single
/// writes answer the same row in its one-operation case, so a save, a move and a delete cannot
/// describe themselves one way alone and another way inside a transaction.
/// </remarks>
/// <param name="Key">The canonical key the write was about; a move names the key it moved to.</param>
/// <param name="Operation">Which write the row is about.</param>
/// <param name="Outcome">What became of it, or — before the commit — that it is staged.</param>
/// <param name="Hash">The revision's hash, when the write left one.</param>
/// <param name="Diagnostics">The compiler's author-facing answer, ordered by template position.</param>
/// <param name="Below">
/// The keys below a key a move named, when that is why it refused, so a page can say what to move
/// first; empty for every other row.
/// </param>
public sealed record PageWriteResult(
    string Key,
    PageWriteOperation Operation,
    PageWriteOutcome Outcome,
    string? Hash,
    IReadOnlyList<PageDiagnostic> Diagnostics,
    IReadOnlyList<string>? Below = null) {

    /// <summary>Whether the compiler found an error, which leaves the page unservable until it is fixed.</summary>
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);

    /// <summary>Whether the compiler found a warning, which does not stop the page being served.</summary>
    public bool HasWarnings => Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Warning);

    /// <summary>Whether the write is stored rather than refused.</summary>
    public bool IsWritten => Outcome is PageWriteOutcome.Saved or PageWriteOutcome.Moved or PageWriteOutcome.Deleted;
}
