// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a save of a page's own source answered with.</summary>
/// <remarks>
/// The answer a page needs to render its own reply: the revision it became, and whether that
/// compiled clean, with warnings, or with errors. A save with errors is still stored — the author's
/// edit is never lost — so the diagnostics are how the page says what to fix, and
/// <see cref="HasErrors"/> says whether the next request can serve it at all.
/// </remarks>
/// <param name="Hash">
/// The new revision's hash, as <see cref="IPageCatalog.RevisionsAsync"/> reports it, or empty when
/// nothing was stored: a credential that is no longer live is <see cref="PageEditOutcome.Revoked"/>.
/// </param>
/// <param name="Diagnostics">The compiler's author-facing answer, ordered by template position.</param>
/// <param name="Outcome">
/// Whether the source was stored, or joined a transaction this render has open and will land with its
/// commit. A compile that faulted still answers here: the source is stored and the fault rethrown.
/// </param>
public sealed record PageSaveResult(
    string Hash,
    IReadOnlyList<PageDiagnostic> Diagnostics,
    PageEditOutcome Outcome = PageEditOutcome.Saved) {

    /// <summary>Whether the save found an error, which leaves the page unservable until it is fixed.</summary>
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);

    /// <summary>Whether the save found a warning, which does not stop the page being served.</summary>
    public bool HasWarnings => Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Warning);
}
