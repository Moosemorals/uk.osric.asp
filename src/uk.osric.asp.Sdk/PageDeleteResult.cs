// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a delete did, or why it deleted nothing.</summary>
public enum PageDeleteOutcome {

    /// <summary>The page's head is a tombstone and its history is kept.</summary>
    Deleted,

    /// <summary>The page was missing or already deleted, so nothing was written.</summary>
    Missing,

    /// <summary>
    /// The delete joined a transaction this render already had open, so nothing is stored yet: it
    /// lands when that transaction commits and is discarded if it is rolled back or disposed.
    /// </summary>
    Staged,

    /// <summary>Another writer held the store, so nothing was written.</summary>
    Busy,
}

/// <summary>What a delete answered with.</summary>
/// <param name="Outcome">What the delete did, or why it deleted nothing.</param>
/// <param name="WasLive">Whether the page was live before the call.</param>
public sealed record PageDeleteResult(PageDeleteOutcome Outcome, bool WasLive) {

    /// <summary>Whether this call tombstoned the page; a missing page answers false rather than failing.</summary>
    public bool Deleted => Outcome == PageDeleteOutcome.Deleted;
}
