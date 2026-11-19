// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>Stores page templates and the image built from each one.</summary>
internal interface IPageStore {

    /// <summary>Reads a page's template and, when it has one, the stored image built from it.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored page, or <see langword="null"/> when no page has that key.</returns>
    Task<PageTemplate?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default);

    /// <summary>Lists every page, live and deleted, with the state of its current image.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every page, ordered by key.</returns>
    /// <remarks>
    /// The state is judged against the running build, so a page whose image came from an earlier one
    /// says so: after a deploy that is every page, until each is next requested.
    /// </remarks>
    Task<IReadOnlyList<PageListRow>> ListPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists every path the store holds a head, a revision or state for.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The distinct paths, ordered by key.</returns>
    /// <remarks>
    /// A path with revisions and no head is a row the listing cannot show; it is included here so a
    /// repair that removes unreachable keys can find it. The store does not decide which paths are
    /// usable, so the caller filters.
    /// </remarks>
    Task<IReadOnlyList<PagePath>> ListStoredPathsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a live page's template text only, without the image stored beside it.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored template, or <see langword="null"/> when no live page has that key.</returns>
    /// <remarks>
    /// An export needs source and nothing else, and loading a page's assembly into memory to copy
    /// its text out of it would be wasted work.
    /// </remarks>
    Task<string?> GetSourceAsync(PagePath path, CancellationToken cancellationToken = default);

    Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default);

    Task<PageTemplate?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PageRevisionSummary>> ListRevisionsAsync(PagePath path, CancellationToken cancellationToken = default);

    Task<PageRevisionTemplate?> GetRevisionTemplateByHashAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a page's template and image in a single write.</summary>
    /// <param name="template">The page to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SavePageAsync(PageTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Records the shipped seed as installed, without storing a revision.</summary>
    /// <param name="path">The canonical key.</param>
    /// <param name="seedHash">The hash of the seed the page's content already equals.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// True when the row recorded the seed; false when the page is missing, deleted or its content
    /// is no longer exactly the seed, so the caller must read it again.
    /// </returns>
    Task<bool> AdoptSeedAsync(PagePath path, string seedHash, CancellationToken cancellationToken = default);

    /// <summary>Saves a revision only when the current live head still has the expected hash.</summary>
    /// <param name="template">The new template and the image built from it.</param>
    /// <param name="expectedHash">The current head hash the caller read.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>False when the page is missing, deleted or has changed since it was read.</returns>
    Task<bool> TrySavePageAsync(PageTemplate template, string expectedHash, CancellationToken cancellationToken = default);

    /// <summary>Stores an image only while the page still holds the template it was built from.</summary>
    /// <param name="revisionId">The revision the image belongs to.</param>
    /// <param name="expectedTemplate">The template the image was built from.</param>
    /// <param name="image">The image to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see langword="true"/> when the result was stored; <see langword="false"/> when the page
    /// changed or disappeared while it compiled, so the caller must read it again.
    /// </returns>
    Task<bool> TrySetImageAsync(string revisionId, string expectedTemplate, PageImage image, CancellationToken cancellationToken = default);

    Task<bool> DeletePageAsync(PagePath path, CancellationToken cancellationToken = default);

    /// <summary>Moves a page, its history and its state to another key in one transaction.</summary>
    /// <param name="from">The key the page has.</param>
    /// <param name="to">The key it should have.</param>
    /// <param name="expectedHash">The live head the caller read.</param>
    /// <param name="cancellationToken">Cancels the move.</param>
    /// <returns>What the store did, or why it moved nothing.</returns>
    /// <remarks>
    /// Every revision is re-filed under the new key and keeps its hash, the anchor and the content
    /// hash follow the head, and nothing is left at the old key. A tombstone at the new key is cleared
    /// first and keeps the history it retained; its state belongs to the dead page and is discarded.
    /// </remarks>
    Task<PageMoveStoreResult> MovePageAsync(
        PagePath from,
        PagePath to,
        string expectedHash,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a deleted page head and all of its revisions.</summary>
    /// <param name="path">The canonical key of a deleted page.</param>
    /// <param name="cancellationToken">Cancels the cleanup.</param>
    /// <returns><see langword="true"/> when a deleted head was scrubbed; otherwise, <see langword="false"/>.</returns>
    Task<bool> ScrubPageAsync(PagePath path, CancellationToken cancellationToken = default);

    /// <summary>Removes every row at a path, whether or not its head is a tombstone.</summary>
    /// <param name="path">The canonical key to remove.</param>
    /// <param name="cancellationToken">Cancels the cleanup.</param>
    /// <returns>What was removed; all three counts are zero when the store held nothing at the path.</returns>
    /// <remarks>
    /// This is the repair's scrub, not the editor's: a row at a key the shell answers can never be
    /// deleted through a page, so it has no tombstone to require. The caller proves the key is not a
    /// page a visitor can reach before it calls this.
    /// </remarks>
    Task<PageScrubResult> ScrubStoredPathAsync(PagePath path, CancellationToken cancellationToken = default);
}
