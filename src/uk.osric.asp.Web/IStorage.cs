// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using uk.osric.asp.Core;

/// <summary>Stores page sources and the compile result belonging to each one.</summary>
internal interface IStorage {

    /// <summary>Reads a page and, when it has one, its stored compile result.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored page, or <see langword="null"/> when no page has that key.</returns>
    Task<StoredPage?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a page and its compile result in a single write.</summary>
    /// <param name="page">The page to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SavePageAsync(StoredPage page, CancellationToken cancellationToken = default);

    /// <summary>Stores a compile result only while the page still holds the content it was built from.</summary>
    /// <param name="path">The canonical key to update.</param>
    /// <param name="expectedContent">The content the compile result belongs to.</param>
    /// <param name="compilation">The compile result to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see langword="true"/> when the result was stored; <see langword="false"/> when the page
    /// changed or disappeared while it compiled, so the caller must read it again.
    /// </returns>
    Task<bool> TrySetCompilationAsync(PagePath path, string expectedContent, PageCompilation compilation, CancellationToken cancellationToken = default);
}
