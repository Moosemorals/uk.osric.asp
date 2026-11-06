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
/// compiling it. A compiler fault is the one case where the source is stored without a compilation:
/// losing the author's edit to report our bug would be the worse failure.
/// </para>
/// <para>
/// Records <c>page.save.duration</c>.
/// </para>
/// </remarks>
internal sealed class PageEditor(
    IPageStore storage,
    PageCompilationService compilations,
    PageMetrics metrics,
    ILogger<PageEditor> logger) {

    /// <summary>Reads a page without compiling it.</summary>
    /// <param name="path">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<StoredPage?> GetAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.GetPageAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<PageHeadSummary>> ListAsync(CancellationToken cancellationToken = default) {
        return storage.ListPagesAsync(cancellationToken);
    }

    public Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.GetHeadAsync(path, cancellationToken);
    }

    public Task<StoredPage?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default) {
        return storage.GetRevisionByHashAsync(hash, cancellationToken);
    }

    public Task<IReadOnlyList<PageRevisionSummary>> ListRevisionsAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.ListRevisionsAsync(path, cancellationToken);
    }

    public Task<PageRevisionSource?> GetRevisionSourceByHashAsync(string hash, CancellationToken cancellationToken = default) {
        return storage.GetRevisionSourceByHashAsync(hash, cancellationToken);
    }

    public Task<bool> DeleteAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.DeletePageAsync(path, cancellationToken);
    }

    public Task<bool> ScrubAsync(PagePath path, CancellationToken cancellationToken = default) {
        return storage.ScrubPageAsync(path, cancellationToken);
    }

    /// <summary>Compiles a page and stores its source and result together.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="content">The template source.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The compiler's author-facing diagnostics, errors and warnings; empty when the page compiled cleanly.</returns>
    public async Task<IReadOnlyList<PageDiagnostic>> SaveAsync(PagePath path, string content, CancellationToken cancellationToken = default) {
        long start = Stopwatch.GetTimestamp();

        PageCompilation compilation;
        try {
            compilation = compilations.Compile(path, content, PageMetrics.ReasonSave, cancellationToken);
        } catch (InvalidOperationException) {
            // A compiler fault is our bug, but the author's edit must survive it. Store the source
            // without a compilation; the next request retries, and the failure still answers 500.
            await storage.SavePageAsync(NewRevision(path, content, null), cancellationToken);
            metrics.RecordSave(path, false, Stopwatch.GetElapsedTime(start));
            throw;
        }

        await storage.SavePageAsync(NewRevision(path, content, compilation), cancellationToken);

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordSave(path, compilation.Image is not null, duration);
        logger.LogInformation("Saved {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Key, duration.TotalMilliseconds,
            Outcome(compilation));

        return compilation.Diagnostics;
    }

    /// <summary>Compiles and saves a page only while its current revision hash still matches.</summary>
    public async Task<ConditionalPageSaveResult> SaveIfRevisionAsync(
        PagePath path,
        string content,
        string expectedRevisionHash,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevisionHash);
        long start = Stopwatch.GetTimestamp();

        PageCompilation compilation;
        try {
            compilation = compilations.Compile(path, content, PageMetrics.ReasonSave, cancellationToken);
        } catch (InvalidOperationException) {
            await storage.TrySavePageAsync(NewRevision(path, content, null), expectedRevisionHash, cancellationToken);
            metrics.RecordSave(path, false, Stopwatch.GetElapsedTime(start));
            throw;
        }

        StoredPage revision = NewRevision(path, content, compilation);
        bool saved = await storage.TrySavePageAsync(revision, expectedRevisionHash, cancellationToken);
        if (!saved) {
            return new ConditionalPageSaveResult(false, null, []);
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordSave(path, compilation.Image is not null, duration);
        logger.LogInformation("Saved {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Key, duration.TotalMilliseconds,
            Outcome(compilation));
        return new ConditionalPageSaveResult(true, revision.Hash, compilation.Diagnostics);
    }

    private static string Outcome(PageCompilation compilation) {
        if (compilation.Image is null) {
            return "with errors";
        }

        return compilation.Diagnostics.Count > 0 ? "compiled with warnings" : "compiled";
    }

    private StoredPage NewRevision(PagePath path, string content, PageCompilation? compilation) {
        string revisionId = Guid.NewGuid().ToString("N");
        DateTimeOffset savedAt = DateTimeOffset.UtcNow;
        return new StoredPage(path, content, compilation) {
            RevisionId = revisionId,
            CreationRuntimeId = compilations.RuntimeId,
            SavedAt = savedAt,
            Hash = PageRevisionHash.Of(content, compilations.RuntimeId, savedAt, revisionId),
        };
    }
}
