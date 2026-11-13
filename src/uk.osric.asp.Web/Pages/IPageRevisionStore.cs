// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Stores one page revision, on a context of its own.</summary>
/// <remarks>
/// A page's task can outlive the request that started it, so a save made from inside a render cannot
/// go through <see cref="IPageStore"/>: that one is scoped to the request's <c>StorageDbContext</c>,
/// which may already be disposed by the time the write lands. The implementation opens its context
/// per save, for the reason <see cref="SqlitePageStateStore"/> does.
/// </remarks>
internal interface IPageRevisionStore {

    /// <summary>Inserts the revision and moves the page's head to it, in one transaction.</summary>
    /// <param name="revision">The revision, already built with its id, hash and image.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The revision that was stored. It is the argument unless another writer took its revision id,
    /// in which case it is the retry — so a caller that reports a hash reports the stored one.
    /// </returns>
    Task<PageTemplate> SaveAsync(PageTemplate revision, CancellationToken cancellationToken = default);

    /// <summary>Inserts the revision and moves the head to it, only while the head still matches.</summary>
    /// <param name="revision">The revision, already built with its id, hash and image.</param>
    /// <param name="expectedHash">The hash the caller saw as the head.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="false"/> when the head is missing, tombstoned or another hash, so nothing was written.</returns>
    Task<bool> TrySaveAsync(PageTemplate revision, string expectedHash, CancellationToken cancellationToken = default);
}
