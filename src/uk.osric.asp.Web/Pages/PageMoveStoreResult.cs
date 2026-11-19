// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>What the store did, or refused to do, when a key was asked to move.</summary>
internal enum PageMoveStoreOutcome {

    /// <summary>The page, its history and its state are under the new key.</summary>
    Moved,

    /// <summary>No live page has the old key.</summary>
    SourceNotFound,

    /// <summary>The live head was not the revision the caller expected.</summary>
    Stale,

    /// <summary>A live page already has the new key.</summary>
    TargetExists,

    /// <summary>Keys live below the old key, so it names a folder, not a page.</summary>
    FolderNotEmpty,

    /// <summary>The page is a library, whose key is in every dependent's closure.</summary>
    IsLibrary,
}

/// <summary>The store's answer to a move, before the capability turns it into a page's answer.</summary>
/// <param name="Outcome">What the store did.</param>
/// <param name="Hash">The head's hash when the page moved; otherwise <see langword="null"/>.</param>
/// <param name="Below">The keys that made the old key a folder, when it did.</param>
internal sealed record PageMoveStoreResult(
    PageMoveStoreOutcome Outcome,
    string? Hash = null,
    IReadOnlyList<string>? Below = null) {

    /// <summary>The answer for an outcome that carries nothing else.</summary>
    /// <param name="outcome">What the store did not do.</param>
    public static PageMoveStoreResult Of(PageMoveStoreOutcome outcome) => new(outcome);
}
