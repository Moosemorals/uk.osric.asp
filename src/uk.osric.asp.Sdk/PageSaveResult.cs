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
/// <param name="Hash">The new revision's hash, as <see cref="IPageCatalog.RevisionsAsync"/> reports it.</param>
/// <param name="Diagnostics">The compiler's author-facing answer, ordered by template position.</param>
public sealed record PageSaveResult(string Hash, IReadOnlyList<PageDiagnostic> Diagnostics) {

    /// <summary>Whether the save found an error, which leaves the page unservable until it is fixed.</summary>
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);

    /// <summary>Whether the save found a warning, which does not stop the page being served.</summary>
    public bool HasWarnings => Diagnostics.Any(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Warning);
}
