// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Editor;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>Reads and saves pages for the editor.</summary>
/// <remarks>
/// <para>
/// A save compiles before it stores, so the page never sits in the database without the result of
/// compiling it. That is <see cref="PageSaver"/>'s job, and it is shared with a page that saves its
/// own source; this class is the editor's view of it, plus the conditional save the editor alone
/// needs (DEC-192).
/// </para>
/// <para>
/// The unconditional save records <c>page.save.duration</c> through the saver; the conditional one
/// shares the saver's accounting so a conflict and a plain save are counted alike.
/// </para>
/// </remarks>
internal sealed class PageEditor(
    IPageStore storage,
    PageSaver saver) {

    /// <summary>Reads a page without compiling it.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<PageTemplate?> GetAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.GetPageAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<PageListing>> ListAsync(CancellationToken cancellationToken = default) {
        return storage.ListPagesAsync(cancellationToken);
    }

    /// <summary>The build that compiled the images this editor serves and saves.</summary>
    public string RuntimeId => saver.RuntimeId;

    public Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.GetHeadAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<PageRevisionSummary>> ListRevisionsAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.ListRevisionsAsync(path, cancellationToken);
    }

    public Task<PageRevisionTemplate?> GetRevisionTemplateByHashAsync(string hash, CancellationToken cancellationToken = default) {
        return storage.GetRevisionTemplateByHashAsync(hash, cancellationToken);
    }

    /// <summary>The revision with that hash, only when it is a revision of this page.</summary>
    /// <param name="path">The page the revision must belong to.</param>
    /// <param name="hash">The canonical hash a listing or a form carried.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The revision, or null when no revision of this page has that hash.</returns>
    /// <remarks>
    /// A hash is an address for one save, not for one page, so a link that carries another page's
    /// hash must read as no revision at all rather than as that page's source.
    /// </remarks>
    public async Task<PageRevisionTemplate?> GetRevisionAsync(
        PagePath path,
        string hash,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        PageRevisionTemplate? revision = await storage.GetRevisionTemplateByHashAsync(hash, cancellationToken);

        return revision is not null && revision.Path == path ? revision : null;
    }

    public Task<bool> DeleteAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.DeletePageAsync(path, cancellationToken);
    }

    public Task<bool> ScrubAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.ScrubPageAsync(path, cancellationToken);
    }

    /// <summary>Builds a page's image and stores it with the template.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="template">The template text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="renderDeadline">
    /// The token of the render that asked for the save, or <see langword="default"/> when no page is
    /// waiting; a compile bounded by it can be given up on without losing the save.
    /// </param>
    /// <returns>
    /// The revision that became the page's head and the compiler's author-facing diagnostics, so a
    /// caller that reports the save can name the revision without a second read.
    /// </returns>
    public Task<SavedRevision> SaveAsync(
        PagePath path,
        string template,
        CancellationToken cancellationToken = default,
        CancellationToken renderDeadline = default) => saver.SaveAsync(path, template, cancellationToken, renderDeadline);

    /// <summary>Compiles and saves a page only while its current revision hash still matches.</summary>
    public Task<ConditionalPageSaveResult> SaveIfRevisionAsync(
        PagePath path,
        string template,
        string expectedRevisionHash,
        CancellationToken cancellationToken = default,
        CancellationToken renderDeadline = default) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevisionHash);

        return saver.SaveIfRevisionAsync(path, template, expectedRevisionHash, cancellationToken, renderDeadline);
    }
}
