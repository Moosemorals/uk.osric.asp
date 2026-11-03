// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Editor;

using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

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
    TemplateCompiler templates,
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
            compilation = templates.Compile(path, content, PageMetrics.ReasonSave, cancellationToken);
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
            CreationRuntimeId = templates.RuntimeId,
            SavedAt = savedAt,
            Hash = PageRevisionHash.Compute(content, templates.RuntimeId, savedAt, revisionId),
        };
    }
}
