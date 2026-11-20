// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a move did, or why it moved nothing.</summary>
public enum PageMoveOutcome {

    /// <summary>The page, its history and its state are under the new key.</summary>
    Moved,

    /// <summary>The two keys are one key, so there was nothing to move.</summary>
    Unchanged,

    /// <summary>The head moved since the move was prepared, so nothing was written.</summary>
    Stale,

    /// <summary>No live page has the old key.</summary>
    SourceNotFound,

    /// <summary>A live page already has the new key, so nothing was written.</summary>
    TargetExists,

    /// <summary>Keys live below the old key, so it names a folder and a folder is a batch of moves.</summary>
    FolderNotEmpty,

    /// <summary>Another writer held the store, so nothing was moved.</summary>
    Busy,

    /// <summary>
    /// The move joined a transaction this render already had open, so nothing is stored yet: it lands
    /// when that transaction commits and is discarded if it is rolled back or disposed.
    /// </summary>
    Staged,

    /// <summary>
    /// The credential the move was made with is no longer live, so nothing was moved. A render never
    /// answers this: only a job borrows a credential it has to keep proving.
    /// </summary>
    Revoked,
}

/// <summary>What a move answered with.</summary>
/// <remarks>
/// A move is a rename, not an edit: every revision is re-filed under the new key with the hash it was
/// saved with, and nothing is left at the old key. The old key then answers as a key that never had a
/// page, so a link to it is not found and a versioned link answers at the new key or not at all.
/// </remarks>
/// <param name="Outcome">What the move did.</param>
/// <param name="Hash">
/// The head's hash when the page moved — the hash it already had, because the head is re-filed rather
/// than rewritten — and <see langword="null"/> for every other outcome.
/// </param>
/// <param name="Below">
/// The keys below the old key when it named a folder, so the page can say what to move first;
/// otherwise empty.
/// </param>
public sealed record PageMoveResult(
    PageMoveOutcome Outcome,
    string? Hash,
    IReadOnlyList<string> Below);
