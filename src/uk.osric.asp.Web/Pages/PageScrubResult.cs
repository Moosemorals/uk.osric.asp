// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>What one path-wide scrub removed.</summary>
/// <param name="Head">Whether a head row was removed, live or a tombstone.</param>
/// <param name="Revisions">How many revisions were removed.</param>
/// <param name="StateKeys">How many page-state rows were removed.</param>
internal sealed record PageScrubResult(bool Head, int Revisions, int StateKeys) {

    /// <summary>Whether the store held anything at the path.</summary>
    public bool Any => Head || Revisions > 0 || StateKeys > 0;
}
