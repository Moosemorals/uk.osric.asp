// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Editor;

using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>The outcome of a save that applies only while the head still has the expected revision.</summary>
internal sealed record ConditionalPageSaveResult(
    bool Saved,
    string? RevisionHash,
    IReadOnlyList<PageDiagnostic> Diagnostics);

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
    PageImageBuilder images,
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
    /// <returns>
    /// The revision that became the page's head and the compiler's author-facing diagnostics, so a
    /// caller that reports the save can name the revision without a second read.
    /// </returns>
    public Task<SavedRevision> SaveAsync(
        PagePath path,
        string template,
        CancellationToken cancellationToken = default) => saver.SaveAsync(path, template, cancellationToken);

    /// <summary>Compiles and saves a page only while its current revision hash still matches.</summary>
    public async Task<ConditionalPageSaveResult> SaveIfRevisionAsync(
        PagePath path,
        string template,
        string expectedRevisionHash,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevisionHash);
        long start = Stopwatch.GetTimestamp();

        PageImage image;
        try {
            image = images.Build(path, template, PageMetrics.ReasonSave, cancellationToken);
        } catch (InvalidOperationException) {
            await storage.TrySavePageAsync(PageTemplate.NewRevision(path, template, null, images.RuntimeId), expectedRevisionHash, cancellationToken);
            saver.RecordSaveFailed(path, start);
            throw;
        }

        PageTemplate revision = PageTemplate.NewRevision(path, template, image, images.RuntimeId);
        bool saved = await storage.TrySavePageAsync(revision, expectedRevisionHash, cancellationToken);
        if (!saved) {
            return new ConditionalPageSaveResult(false, null, []);
        }

        saver.RecordSaved(path, image, start);

        return new ConditionalPageSaveResult(true, revision.Hash, image.Diagnostics);
    }
}
