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
/// compiling it. A compiler fault is the one case where the template is stored without an image:
/// losing the author's edit to report our bug would be the worse failure.
/// </para>
/// <para>
/// Records <c>page.save.duration</c>.
/// </para>
/// </remarks>
internal sealed class PageEditor(
    IPageStore storage,
    PageImageBuilder images,
    PageMetrics metrics,
    ILogger<PageEditor> logger) {

    /// <summary>Reads a page without compiling it.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<PageTemplate?> GetAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.GetPageAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<PageHeadSummary>> ListAsync(CancellationToken cancellationToken = default) {
        return storage.ListPagesAsync(cancellationToken);
    }

    public Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.GetHeadAsync(path, cancellationToken);
    }

    public Task<PageTemplate?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default) {
        return storage.GetRevisionByHashAsync(hash, cancellationToken);
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
    /// <returns>The compiler's author-facing diagnostics, errors and warnings; empty when the page compiled cleanly.</returns>
    public async Task<IReadOnlyList<PageDiagnostic>> SaveAsync(PagePath path, string template, CancellationToken cancellationToken = default) {
        long start = Stopwatch.GetTimestamp();

        PageImage image;
        try {
            image = images.Build(path, template, PageMetrics.ReasonSave, cancellationToken);
        } catch (InvalidOperationException) {
            // A compiler fault is our bug, but the author's edit must survive it. Store the template
            // without an image; the next request retries, and the failure still answers 500.
            await storage.SavePageAsync(NewRevision(path, template, null), cancellationToken);
            metrics.RecordSave(path, false, Stopwatch.GetElapsedTime(start));
            throw;
        }

        await storage.SavePageAsync(NewRevision(path, template, image), cancellationToken);

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordSave(path, image.Bytes is not null, duration);
        logger.LogInformation("Saved {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Key, duration.TotalMilliseconds,
            Outcome(image));

        return image.Diagnostics;
    }

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
            await storage.TrySavePageAsync(NewRevision(path, template, null), expectedRevisionHash, cancellationToken);
            metrics.RecordSave(path, false, Stopwatch.GetElapsedTime(start));
            throw;
        }

        PageTemplate revision = NewRevision(path, template, image);
        bool saved = await storage.TrySavePageAsync(revision, expectedRevisionHash, cancellationToken);
        if (!saved) {
            return new ConditionalPageSaveResult(false, null, []);
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordSave(path, image.Bytes is not null, duration);
        logger.LogInformation("Saved {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Key, duration.TotalMilliseconds,
            Outcome(image));
        return new ConditionalPageSaveResult(true, revision.Hash, image.Diagnostics);
    }

    private static string Outcome(PageImage image) {
        if (image.Bytes is null) {
            return "with errors";
        }

        return image.Diagnostics.Count > 0 ? "compiled with warnings" : "compiled";
    }

    private PageTemplate NewRevision(PagePath path, string template, PageImage? image) {
        string revisionId = Guid.NewGuid().ToString("N");
        DateTimeOffset savedAt = DateTimeOffset.UtcNow;
        return new PageTemplate(path, template, image) {
            RevisionId = revisionId,
            CreationRuntimeId = images.RuntimeId,
            SavedAt = savedAt,
            Hash = PageRevisionHash.Of(template, images.RuntimeId, savedAt, revisionId),
        };
    }
}
