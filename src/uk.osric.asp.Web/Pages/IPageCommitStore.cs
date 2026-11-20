// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>The kind of write one operation of a commit is.</summary>
internal enum PageCommitKind {

    /// <summary>A revision, stored only while the head still has the expected hash.</summary>
    Save,

    /// <summary>A revision, stored only while the page has no live source.</summary>
    Create,

    /// <summary>A revision stored unconditionally: the bound save, and a seed install that replaces.</summary>
    Put,

    /// <summary>A move of a page, its history and its state.</summary>
    Move,

    /// <summary>A tombstone, keeping the history.</summary>
    Delete,

    /// <summary>A seed recorded as installed, with no revision.</summary>
    Adopt,
}

/// <summary>What the store did with one operation of a commit.</summary>
internal enum PageCommitOutcome {

    /// <summary>The revision was stored.</summary>
    Saved,

    /// <summary>The page already held the seed, so nothing was recorded.</summary>
    Unchanged,

    /// <summary>The head was not the revision the operation expected.</summary>
    Stale,

    /// <summary>The page, its history and its state are under the new key.</summary>
    Moved,

    /// <summary>The head is a tombstone and its history is kept.</summary>
    Deleted,

    /// <summary>No live page has that key.</summary>
    SourceNotFound,

    /// <summary>A live page already has the key a move named.</summary>
    TargetExists,

    /// <summary>Keys live below a key a move named, so it names a folder.</summary>
    FolderNotEmpty,

    /// <summary>The content is not the seed, so nothing was recorded.</summary>
    Refused,

    /// <summary>An earlier operation refused, so this one was never attempted.</summary>
    NotAttempted,

    /// <summary>Another writer held the store, so nothing was written.</summary>
    Busy,
}

/// <summary>One write in a commit, already compiled and ready for the store.</summary>
/// <param name="Kind">What the operation does.</param>
/// <param name="Key">The key it is about; a move names the key the page should have.</param>
/// <param name="ExpectedHash">The head the operation was prepared against, when it has one.</param>
/// <param name="Revision">The revision to store, for a save or a create.</param>
/// <param name="From">The key a move leaves, when the operation is a move.</param>
/// <param name="Target">The key a move names, when the operation is a move.</param>
/// <param name="SeedHash">The seed a save installs, or the seed an adopt records.</param>
internal sealed record PageCommit(
    PageCommitKind Kind,
    PagePath Key,
    string? ExpectedHash = null,
    PageTemplate? Revision = null,
    PagePath? From = null,
    PagePath? Target = null,
    string? SeedHash = null);

/// <summary>What the store did with one operation.</summary>
/// <param name="Key">The key the operation was about.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="Hash">The revision's hash, when the operation left one.</param>
/// <param name="Below">The keys below a key a move named, when that is why it refused.</param>
/// <param name="Stored">The revision as the store kept it, after a retry took a fresh id.</param>
internal sealed record PageCommitAnswer(
    PagePath Key,
    PageCommitOutcome Outcome,
    string? Hash = null,
    IReadOnlyList<string>? Below = null,
    PageTemplate? Stored = null);

/// <summary>Writes several operations as one store transaction, all of them or none.</summary>
/// <remarks>
/// Every condition is proved before anything is written, so a refusal names every operation that
/// refused rather than only the first, and a refusal writes nothing at all. The transaction is the
/// store's own: it is opened once, held for statements only — a compile never happens inside it — and
/// a commit that cannot take the writer answers <see cref="PageCommitOutcome.Busy"/> instead of
/// waiting the busy timeout out silently.
/// </remarks>
internal interface IPageCommitStore {

    /// <summary>Writes every operation in one transaction, or none of them.</summary>
    /// <param name="operations">The operations, in the order the page staged them.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>One answer per operation, in the same order.</returns>
    Task<IReadOnlyList<PageCommitAnswer>> CommitAsync(
        IReadOnlyList<PageCommit> operations,
        CancellationToken cancellationToken = default);
}
