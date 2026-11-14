// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Editor;

using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>What a save stored, and what compiling it reported.</summary>
/// <param name="Revision">The revision that became the page's head.</param>
/// <param name="Diagnostics">The compiler's author-facing answer; empty when the page compiled cleanly.</param>
internal sealed record SavedRevision(PageTemplate Revision, IReadOnlyList<PageDiagnostic> Diagnostics);

/// <summary>The outcome of a save that applies only while the head still has the expected revision.</summary>
internal sealed record ConditionalPageSaveResult(
    bool Saved,
    string? RevisionHash,
    IReadOnlyList<PageDiagnostic> Diagnostics);

/// <summary>Compiles a template and stores it as a revision.</summary>
/// <remarks>
/// <para>
/// This is the one save: the editor, the seed restore, the assistant's save tools and a page saving
/// its own source all end up here. A save compiles before it stores, so the page never sits in the
/// database without the result of compiling it, and a compiler fault is the one case where the
/// template is stored without an image — losing the author's edit to report our bug would be the
/// worse failure (DEC-20, DEC-23, DEC-178).
/// </para>
/// <para>
/// It is a singleton because a page's save runs after the request that started it may have gone, and
/// it writes through <see cref="IPageRevisionStore"/> for the same reason (DEC-172, DEC-192). A
/// caller that owns a request's context still reads through <see cref="IPageStore"/>; only the write
/// is shared.
/// </para>
/// <para>
/// Records <c>page.save.duration</c>.
/// </para>
/// </remarks>
internal sealed class PageSaver(
    IPageRevisionStore store,
    PageImageBuilder images,
    PageLibraries libraries,
    PageMetrics metrics,
    ILogger<PageSaver> logger) {

    /// <summary>The build that compiled the images this saver stores.</summary>
    public string RuntimeId => images.RuntimeId;

    /// <summary>Builds a page's image and stores it with the template.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="template">The template text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="renderDeadline">
    /// The render's deadline when the save was made from inside a render. It bounds the wait for a
    /// compile slot and nothing else: the compile itself and the insert are left to finish, because
    /// losing the author's edit is the one loss this site refuses (DEC-191).
    /// </param>
    /// <returns>The revision the save stored and the compiler's author-facing diagnostics.</returns>
    public async Task<SavedRevision> SaveAsync(
        PagePath path,
        string template,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline = default) {
        long start = Stopwatch.GetTimestamp();
        // A save compiles, so it reads the libraries the page may name. A compile is rare and a
        // library is small; a source read here is cheaper than a page that cannot find its helpers.
        IReadOnlyList<PageLibrary> available = await libraries.LiveAsync(cancellationToken);

        PageImage image;
        try {
            image = images.Build(path, template, PageMetrics.ReasonSave, available, cancellationToken, renderDeadline);
        } catch (InvalidOperationException) {
            // A compiler fault is our bug, but the author's edit must survive it. Store the template
            // without an image; the next request retries, and the failure still answers 500.
            PageTemplate faulted = PageTemplate.NewRevision(path, template, null, images.RuntimeId);
            await store.SaveAsync(faulted, cancellationToken);
            RecordSaveFailed(path, start);
            throw;
        }

        PageTemplate revision = PageTemplate.NewRevision(path, template, image, images.RuntimeId);
        PageTemplate stored = await store.SaveAsync(revision, cancellationToken);

        RecordSaved(path, image, start);

        return new SavedRevision(stored, image.Diagnostics);
    }

    /// <summary>Compiles and stores a page only while its current revision hash still matches.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="template">The template text.</param>
    /// <param name="expectedRevisionHash">The hash the caller saw as the head.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="renderDeadline">The render's deadline, when the save was made from inside a render.</param>
    /// <returns>The stored revision and diagnostics, or <see cref="ConditionalPageSaveResult.Saved"/> false when the head moved.</returns>
    public async Task<ConditionalPageSaveResult> SaveIfRevisionAsync(
        PagePath path,
        string template,
        string expectedRevisionHash,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline = default) {
        long start = Stopwatch.GetTimestamp();
        IReadOnlyList<PageLibrary> available = await libraries.LiveAsync(cancellationToken);

        PageImage image;
        try {
            image = images.Build(path, template, PageMetrics.ReasonSave, available, cancellationToken, renderDeadline);
        } catch (InvalidOperationException) {
            // A compiler fault still stores the source: losing the author's edit to report our bug
            // is the worse failure (DEC-20, DEC-23, DEC-178).
            _ = await store.TrySaveAsync(
                PageTemplate.NewRevision(path, template, null, images.RuntimeId), expectedRevisionHash, cancellationToken);
            RecordSaveFailed(path, start);
            throw;
        }

        PageTemplate revision = PageTemplate.NewRevision(path, template, image, images.RuntimeId);
        bool saved = await store.TrySaveAsync(revision, expectedRevisionHash, cancellationToken);
        if (!saved) {
            return new ConditionalPageSaveResult(false, null, []);
        }

        RecordSaved(path, image, start);

        return new ConditionalPageSaveResult(true, revision.Hash, image.Diagnostics);
    }

    /// <summary>Records a save that kept its source but produced no image.</summary>
    /// <param name="path">The page that was saved.</param>
    /// <param name="start">When the save started.</param>
    internal void RecordSaveFailed(PagePath path, long start) =>
        metrics.RecordSave(path, false, Stopwatch.GetElapsedTime(start));

    /// <summary>Records a save made through another path, so every save is counted alike.</summary>
    /// <param name="path">The page that was saved.</param>
    /// <param name="image">What compiling it produced.</param>
    /// <param name="start">When the save started.</param>
    /// <remarks>
    /// The editor's conditional save compiles through the same builder but writes through its own
    /// request's store, so it shares this line rather than repeating it.
    /// </remarks>
    internal void RecordSaved(PagePath path, PageImage image, long start) {
        TimeSpan duration = Stopwatch.GetElapsedTime(start);
        metrics.RecordSave(path, image.Bytes is not null || image.Kind is PageKind.Text, duration);
        logger.LogInformation("Saved {Path} in {ElapsedMs:0.0} ms ({Outcome})", path.Key, duration.TotalMilliseconds,
            Outcome(image));
    }

    private static string Outcome(PageImage image) {
        if (image.Kind is PageKind.Text) {
            return "stored as source";
        }

        if (image.Kind is PageKind.Library) {
            return image.Diagnostics.Count > 0 ? "library with issues" : "library";
        }

        if (image.Bytes is null) {
            return "with errors";
        }

        return image.Diagnostics.Count > 0 ? "compiled with warnings" : "compiled";
    }
}
