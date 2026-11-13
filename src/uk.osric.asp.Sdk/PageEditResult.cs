// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What happened to an edit.</summary>
public enum PageEditOutcome {

    /// <summary>The source was stored as a new revision.</summary>
    Saved,

    /// <summary>The head moved since the form was rendered, so nothing was written.</summary>
    Stale,

    /// <summary>The page already held the source, so nothing was written.</summary>
    Unchanged,
}

/// <summary>What an edit answered with.</summary>
/// <remarks>
/// A save with errors is still stored — the author's edit is never lost — so the diagnostics are how
/// the page says what to fix, and <see cref="HasErrors"/> says whether the next request can serve it.
/// </remarks>
/// <param name="Outcome">Whether the source was stored, refused as stale, or already current.</param>
/// <param name="Hash">The revision's hash when something was stored or already current; otherwise null.</param>
/// <param name="Diagnostics">The compiler's author-facing answer, ordered by template position.</param>
public sealed record PageEditResult(
    PageEditOutcome Outcome,
    string? Hash,
    IReadOnlyList<PageDiagnostic> Diagnostics) {

    /// <summary>Whether the save found an error, which leaves the page unservable until it is fixed.</summary>
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);

    /// <summary>Whether the save found a warning, which does not stop the page being served.</summary>
    public bool HasWarnings => Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Warning);
}
