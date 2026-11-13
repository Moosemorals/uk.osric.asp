// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>The one test a write that names its source asks: would it change anything?</summary>
/// <remarks>
/// <para>
/// A restore, a seed install and an edit all end in the same comparison, and all three refuse the
/// write that would leave the live source exactly as it is: a revision whose text matches is noise in
/// a history whose job is to record changes (DEC-192).
/// </para>
/// <para>
/// A page that is missing or tombstoned has no live source, so bringing it back is a change whatever
/// the text says. The comparison is ordinal because the source is compiled as it was written, so a
/// change of case or of line ending is a different page.
/// </para>
/// </remarks>
public static class PageSourceChange {

    /// <summary>Whether storing a source would leave the page's source as it is.</summary>
    /// <param name="live">The live source, or <see langword="null"/> when the page is missing or deleted.</param>
    /// <param name="chosen">The source a write would store.</param>
    /// <returns><see langword="true"/> when the page already holds exactly this text.</returns>
    public static bool IsNoOp(string? live, string chosen) {
        ArgumentNullException.ThrowIfNull(chosen);

        return live is not null && string.Equals(live, chosen, StringComparison.Ordinal);
    }
}
