// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Editor;

using System.Diagnostics;
using System.Runtime.ExceptionServices;
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

/// <summary>A revision built and not yet stored, with the fault that stopped it compiling.</summary>
/// <param name="Revision">The revision the commit would store.</param>
/// <param name="Diagnostics">The compiler's author-facing answer; empty when it compiled cleanly.</param>
/// <param name="Fault">
/// The compiler's own failure, when it threw rather than answering diagnostics. The revision carries
/// no image, and the caller stores it anyway before rethrowing: the author's edit outlives our bug.
/// </param>
internal sealed record StagedRevision(PageTemplate Revision, IReadOnlyList<PageDiagnostic> Diagnostics, Exception? Fault);

/// <summary>Compiles a template and stores it as a revision.</summary>
/// <remarks>
/// <para>
/// This is the one save: the editor, the seed restore, the assistant's save tools and a page saving
/// its own source all end up here. A save compiles before it stores, so the page never sits in the
/// database without the result of compiling it, and a compiler fault is the one case where the
/// template is stored without an image — losing the author's edit to report our bug would be the
/// worse failure (`pages-a-compiler-fault-is-ours`, `pages-a-warning-never-stops-a-page`).
/// </para>
/// <para>
/// It is a singleton because a page's save runs after the request that started it may have gone, and
/// it writes through <see cref="IPageRevisionStore"/> for the same reason (`pages-the-store-is-read-through-one-seam`, `pages-a-page-may-save-itself`). A
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
    /// losing the author's edit is the one loss this site refuses (`pages-a-page-may-save-itself`).
    /// </param>
    /// <returns>The revision the save stored and the compiler's author-facing diagnostics.</returns>
    public Task<SavedRevision> SaveAsync(
        PagePath path,
        string template,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline = default) =>
        SaveCoreAsync(path, template, null, cancellationToken, renderDeadline);

    /// <summary>Compiles and stores a seed's bundled source, recording it as the page's anchor.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="template">The bundled source.</param>
    /// <param name="seedHash">The hash of the seed being installed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="renderDeadline">The render's deadline, when the save was made from inside a render.</param>
    /// <returns>The revision the save stored and the compiler's author-facing diagnostics.</returns>
    public Task<SavedRevision> SaveSeedAsync(
        PagePath path,
        string template,
        string seedHash,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedHash);

        return SaveCoreAsync(path, template, seedHash, cancellationToken, renderDeadline);
    }

    /// <summary>Compiles and stores a page only while its current revision hash still matches.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="template">The template text.</param>
    /// <param name="expectedRevisionHash">The hash the caller saw as the head.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="renderDeadline">The render's deadline, when the save was made from inside a render.</param>
    /// <returns>The stored revision and diagnostics, or <see cref="ConditionalPageSaveResult.Saved"/> false when the head moved.</returns>
    public Task<ConditionalPageSaveResult> SaveIfRevisionAsync(
        PagePath path,
        string template,
        string expectedRevisionHash,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline = default) =>
        SaveIfRevisionCoreAsync(path, template, expectedRevisionHash, null, cancellationToken, renderDeadline);

    /// <summary>A conditional seed write: stores the seed only while the head still has the expected hash.</summary>
    /// <param name="path">The canonical key to save.</param>
    /// <param name="template">The bundled source.</param>
    /// <param name="expectedRevisionHash">The hash the caller saw as the head.</param>
    /// <param name="seedHash">The hash of the seed being installed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="renderDeadline">The render's deadline, when the save was made from inside a render.</param>
    /// <returns>The stored revision and diagnostics, or <see cref="ConditionalPageSaveResult.Saved"/> false when the head moved.</returns>
    public Task<ConditionalPageSaveResult> SaveSeedIfRevisionAsync(
        PagePath path,
        string template,
        string expectedRevisionHash,
        string seedHash,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedHash);

        return SaveIfRevisionCoreAsync(path, template, expectedRevisionHash, seedHash, cancellationToken, renderDeadline);
    }

    /// <summary>Compiles a template without storing it, for a transaction to commit later.</summary>
    /// <param name="path">The canonical key the revision is for.</param>
    /// <param name="template">The template text.</param>
    /// <param name="seedHash">The seed this write installs, or <see langword="null"/>.</param>
    /// <param name="stagedLibraries">
    /// Libraries the same transaction staged before this save, which take the place of the stored
    /// ones with the same key; <see langword="null"/> when nothing earlier in the transaction is a
    /// library.
    /// </param>
    /// <param name="cancellationToken">Cancels the compile.</param>
    /// <param name="renderDeadline">The render's deadline, when the compile was asked for by a page.</param>
    /// <returns>The revision, its diagnostics, and the compiler's fault when it threw.</returns>
    /// <remarks>
    /// Staging is a compile and nothing else: no lock is held while it waits for a slot, which is what
    /// lets a commit stay statements only.
    /// </remarks>
    public async Task<StagedRevision> BuildAsync(
        PagePath path,
        string template,
        string? seedHash,
        IReadOnlyList<PageLibrary>? stagedLibraries,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline = default) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(template);

        // A save compiles, so it reads the libraries the page may name. A compile is rare and a
        // library is small; a source read here is cheaper than a page that cannot find its helpers.
        IReadOnlyList<PageLibrary> available = await LibrariesAsync(stagedLibraries, cancellationToken);

        PageImage image;
        try {
            image = images.Build(path, template, PageMetrics.ReasonSave, available, cancellationToken, renderDeadline);
        } catch (InvalidOperationException fault) {
            // A compiler fault is our bug, but the author's edit must survive it: the caller stores
            // the template without an image, then rethrows (`pages-a-compiler-fault-is-ours`).
            PageTemplate faulted = PageTemplate.NewRevision(path, template, null, images.RuntimeId, seedHash);
            return new StagedRevision(faulted, [], fault);
        }

        PageTemplate revision = PageTemplate.NewRevision(path, template, image, images.RuntimeId, seedHash);

        return new StagedRevision(revision, image.Diagnostics, null);
    }

    /// <summary>Writes staged operations as one transaction.</summary>
    /// <param name="operations">The operations to commit.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    public Task<IReadOnlyList<PageCommitAnswer>> CommitAsync(
        IReadOnlyList<PageCommit> operations,
        CancellationToken cancellationToken = default) => store.CommitAsync(operations, cancellationToken);

    private async Task<SavedRevision> SaveCoreAsync(
        PagePath path,
        string template,
        string? seedHash,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline) {
        long start = Stopwatch.GetTimestamp();

        StagedRevision staged = await BuildAsync(path, template, seedHash, null, cancellationToken, renderDeadline);
        PageTemplate stored = await store.SaveAsync(staged.Revision, cancellationToken);

        if (staged.Fault is { } fault) {
            RecordSaveFailed(path, start);
            ExceptionDispatchInfo.Capture(fault).Throw();
        }

        RecordSaved(path, staged.Revision.Image!, start);

        return new SavedRevision(stored, staged.Diagnostics);
    }

    private async Task<ConditionalPageSaveResult> SaveIfRevisionCoreAsync(
        PagePath path,
        string template,
        string expectedRevisionHash,
        string? seedHash,
        CancellationToken cancellationToken,
        CancellationToken renderDeadline) {
        long start = Stopwatch.GetTimestamp();

        StagedRevision staged = await BuildAsync(path, template, seedHash, null, cancellationToken, renderDeadline);
        bool saved = await store.TrySaveAsync(staged.Revision, expectedRevisionHash, cancellationToken);

        if (staged.Fault is { } fault) {
            // A compiler fault still stores the source: losing the author's edit to report our bug
            // is the worse failure (`pages-a-compiler-fault-is-ours`, `pages-a-warning-never-stops-a-page`).
            RecordSaveFailed(path, start);
            ExceptionDispatchInfo.Capture(fault).Throw();
        }

        if (!saved) {
            return new ConditionalPageSaveResult(false, null, []);
        }

        RecordSaved(path, staged.Revision.Image!, start);

        return new ConditionalPageSaveResult(true, staged.Revision.Hash, staged.Diagnostics);
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

    // A staged save sees the libraries staged before it as well as the store's, so a transaction
    // that writes a library and a page using it compiles the page against the new library when the
    // library was staged first. Nothing is reordered: the order the page made the calls is the
    // order it gets.
    private async Task<IReadOnlyList<PageLibrary>> LibrariesAsync(IReadOnlyList<PageLibrary>? staged, CancellationToken cancellationToken) {
        IReadOnlyList<PageLibrary> stored = await libraries.LiveAsync(cancellationToken);
        if (staged is null or { Count: 0 }) {
            return stored;
        }

        Dictionary<string, PageLibrary> merged = [];
        foreach (PageLibrary library in stored) {
            merged[library.Key.Key] = library;
        }

        foreach (PageLibrary library in staged) {
            merged[library.Key.Key] = library;
        }

        return [.. merged.Values.OrderBy(library => library.Key.Key, StringComparer.Ordinal)];
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
