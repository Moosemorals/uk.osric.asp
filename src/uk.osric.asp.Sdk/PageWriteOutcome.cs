// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What became of one write in a transaction.</summary>
public enum PageWriteOutcome {

    /// <summary>The write compiled and is waiting for the commit; nothing is stored yet.</summary>
    Staged,

    /// <summary>The source was stored as a new revision.</summary>
    Saved,

    /// <summary>The page already held the source, so nothing was written.</summary>
    Unchanged,

    /// <summary>The head moved since the form was rendered, so nothing was written.</summary>
    Stale,

    /// <summary>The page, its history and its state are under the new key.</summary>
    Moved,

    /// <summary>The page's head is a tombstone and its history is kept.</summary>
    Deleted,

    /// <summary>No live page has that key.</summary>
    SourceNotFound,

    /// <summary>A live page already has the key a move named.</summary>
    TargetExists,

    /// <summary>Keys live below a key a move named, so it names a folder.</summary>
    FolderNotEmpty,

    /// <summary>The key names no revision, or no seed, so there was nothing to store.</summary>
    Missing,

    /// <summary>The page's content is not the seed, or the seed is not the release's, so nothing was recorded.</summary>
    Refused,

    /// <summary>Another writer held the store, so nothing was written.</summary>
    Busy,

    /// <summary>
    /// The write was never attempted: an earlier operation was refused, or the transaction was rolled
    /// back or disposed before it committed.
    /// </summary>
    NotAttempted,

    /// <summary>
    /// The operation was applied and then a later refusal rolled the whole transaction back, so
    /// nothing it wrote is in the store.
    /// </summary>
    RolledBack,
}
