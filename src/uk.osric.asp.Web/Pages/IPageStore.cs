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

    Task<IReadOnlyList<PageHeadSummary>> ListPagesAsync(CancellationToken cancellationToken = default);

    Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default);

    Task<PageTemplate?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PageRevisionSummary>> ListRevisionsAsync(PagePath path, CancellationToken cancellationToken = default);

    Task<PageRevisionTemplate?> GetRevisionTemplateByHashAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a page's template and image in a single write.</summary>
    /// <param name="template">The page to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SavePageAsync(PageTemplate template, CancellationToken cancellationToken = default);

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

    /// <summary>Removes a deleted page head and all of its revisions.</summary>
    /// <param name="path">The canonical key of a deleted page.</param>
    /// <param name="cancellationToken">Cancels the cleanup.</param>
    /// <returns><see langword="true"/> when a deleted head was scrubbed; otherwise, <see langword="false"/>.</returns>
    Task<bool> ScrubPageAsync(PagePath path, CancellationToken cancellationToken = default);
}
