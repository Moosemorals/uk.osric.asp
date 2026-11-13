// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>One node of the page store as a reader sees it: a key, a folder above one, or a route.</summary>
/// <param name="Path">The canonical key.</param>
/// <param name="IsFolder">Whether any key lives beneath it, so that it can be listed.</param>
/// <param name="IsPage">Whether a page or a shell file is stored at the key itself.</param>
/// <param name="IsShellFile">Whether the site's own routes answer the key, so no page is stored there.</param>
/// <param name="IsDeleted">Whether the key is a tombstone that still has its history.</param>
/// <param name="CanWrite">Whether a save at the key would be accepted, which is the permission column.</param>
/// <param name="State">What the page's current revision compiled to, or null when the key holds no page.</param>
/// <remarks>
/// The node carries facts rather than words: the permission column and the compile state are formatted
/// by whoever shows them, with <see cref="PageMode"/> and <see cref="PageImageStatus"/>, so a tool and
/// an applet cannot describe one page differently.
/// </remarks>
internal sealed record PageNode(
    PagePath Path,
    bool IsFolder,
    bool IsPage,
    bool IsShellFile,
    bool IsDeleted,
    bool CanWrite,
    PageImageState? State);

/// <summary>What one key holds.</summary>
/// <param name="Path">The canonical key.</param>
/// <param name="IsShellFile">Whether the site's own routes answer the key.</param>
/// <param name="CanWrite">Whether a save at the key would be accepted.</param>
/// <param name="State">What the page's current revision compiled to, or null for a shell file.</param>
/// <param name="Text">The page's stored template, or the shell file's record of what the route does.</param>
/// <param name="Page">The page and the image built from it, or null when the key is a shell file.</param>
internal sealed record PageRead(
    PagePath Path,
    bool IsShellFile,
    bool CanWrite,
    PageImageState? State,
    string Text,
    PageTemplate? Page);

/// <summary>A page's history: its head, and every revision it retains.</summary>
/// <param name="Exists">Whether a head exists, a tombstone's included.</param>
/// <param name="IsDeleted">Whether the head is a tombstone.</param>
/// <param name="Revisions">The retained revisions, newest first.</param>
internal sealed record PageHistory(bool Exists, bool IsDeleted, IReadOnlyList<PageRevisionSummary> Revisions) {

    /// <summary>The answer for a key no page has ever been saved at.</summary>
    public static PageHistory Missing { get; } = new(false, false, []);
}

/// <summary>The page store as everything that reads it sees it.</summary>
/// <remarks>
/// One seam, so the assistant's tools and its virtual shell cannot decide differently what the store
/// contains: which keys are reserved, which tombstones are visible, what order keys come in, what a
/// scope means and what a revision is. Writes are deliberately not here; they stay with the saver.
/// </remarks>
internal interface IPageStoreReader {

    /// <summary>Every node of the tree, in ordinal order, with tombstones when they are asked for.</summary>
    /// <param name="includeDeleted">Whether a key that is only a tombstone is a node in the answer.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<IReadOnlyList<PageNode>> TreeAsync(bool includeDeleted, CancellationToken cancellationToken);

    /// <summary>What the pages and shell records inside the scopes hold, in key order.</summary>
    /// <param name="scopes">The paths to read, each saying whether it is itself read and whether it descends.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<IReadOnlyList<PageRead>> SourcesAsync(IReadOnlyList<PageScope> scopes, CancellationToken cancellationToken);

    /// <summary>What one key holds, or null when nothing is stored at it.</summary>
    /// <param name="path">The node to read.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<PageRead?> ReadAsync(PagePath path, CancellationToken cancellationToken);

    /// <summary>A page's head and every revision it retains.</summary>
    /// <param name="path">The page to read the history of; a tombstone's history is answered too.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<PageHistory> HistoryAsync(PagePath path, CancellationToken cancellationToken);

    /// <summary>The revision with that hash, only when it is a revision of this page.</summary>
    /// <param name="path">The page the revision must belong to.</param>
    /// <param name="hash">The canonical hash a listing or a form carried.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<PageRevisionTemplate?> RevisionAsync(PagePath path, string hash, CancellationToken cancellationToken);
}

/// <summary>The store behind the tools and the shell, over the editor and the path policy.</summary>
/// <remarks>
/// Constructed where it is used, the way the shell's view was: it holds no state beyond the two
/// singletons it reads through.
/// </remarks>
/// <param name="pages">The editor, which reads pages, histories and the running build.</param>
/// <param name="paths">The policy, which says what exists and what may be written.</param>
internal sealed class PageStoreReader(PageEditor pages, PagePathPolicy paths) : IPageStoreReader {

    private static readonly PagePath s_root = PagePath.Normalize("/");

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageNode>> TreeAsync(bool includeDeleted, CancellationToken cancellationToken) {
        Dictionary<string, (bool IsDeleted, PageImageState State)> stored = [with(StringComparer.Ordinal)];
        foreach (PageListing head in await pages.ListAsync(cancellationToken)) {
            if (paths.IsReserved(head.Path) || (head.IsDeleted && !includeDeleted)) {
                continue;
            }

            stored[head.Path.Key] = (head.IsDeleted, head.State);
        }

        HashSet<string> reserved = [with(StringComparer.Ordinal), .. paths.Reserved.Select(key => key.Key)];

        SortedSet<string> nodes = [with(StringComparer.Ordinal), s_root.Key];
        foreach (string key in stored.Keys.Concat(reserved)) {
            _ = nodes.Add(key);
            foreach (string folder in PageTree.FoldersAbove(key)) {
                _ = nodes.Add(folder);
            }
        }

        List<PageNode> tree = [];
        foreach (string node in nodes) {
            PagePath path = PagePath.Normalize(node);
            bool isFolder = nodes.Any(other => PageTree.IsBelow(PagePath.Normalize(other), path));
            bool isShellFile = reserved.Contains(path.Key);
            bool isStored = stored.TryGetValue(path.Key, out (bool IsDeleted, PageImageState State) row);

            tree.Add(new PageNode(
                path,
                isFolder,
                IsPage: isShellFile || isStored,
                IsShellFile: isShellFile,
                IsDeleted: isStored && row.IsDeleted,
                paths.CanWrite(path),
                isShellFile || !isStored ? null : row.State));
        }

        return tree;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageRead>> SourcesAsync(
        IReadOnlyList<PageScope> scopes,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(scopes);

        List<PageRead> sources = [];
        foreach (PageNode node in await TreeAsync(includeDeleted: false, cancellationToken)) {
            if (!node.IsPage || !scopes.Any(scope => scope.Contains(node.Path))) {
                continue;
            }

            // A tombstone is never a node here, and a key that is only a folder has nothing to read.
            if (await ReadAsync(node.Path, cancellationToken) is { } read) {
                sources.Add(read);
            }
        }

        return sources;
    }

    /// <inheritdoc/>
    public Task<PageRead?> ReadAsync(PagePath path, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(path);

        return paths.IsReserved(path)
            ? Task.FromResult<PageRead?>(ShellFile(path))
            : ReadPageAsync(path, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PageHistory> HistoryAsync(PagePath path, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(path);

        if (await pages.GetHeadAsync(path, cancellationToken) is not { } head) {
            return PageHistory.Missing;
        }

        IReadOnlyList<PageRevisionSummary> revisions = await pages.ListRevisionsAsync(path, cancellationToken);
        return new PageHistory(true, head.IsDeleted, revisions);
    }

    /// <inheritdoc/>
    public Task<PageRevisionTemplate?> RevisionAsync(PagePath path, string hash, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        return pages.GetRevisionAsync(path, hash, cancellationToken);
    }

    // A shell file is a route the site answers itself: it is readable as a record of what the route
    // does, it is never a stored page, and no save would be accepted at its key.
    private static PageRead ShellFile(PagePath path) =>
        new(path, IsShellFile: true, CanWrite: false, State: null, ShellRoutes.Text(path), Page: null);

    private async Task<PageRead?> ReadPageAsync(PagePath path, CancellationToken cancellationToken) {
        PageTemplate? page = await pages.GetAsync(path, cancellationToken);

        return page is null
            ? null
            : new PageRead(path, IsShellFile: false, paths.CanWrite(path), Describe(page), page.Template, page);
    }

    // The state is the listing's state, judged against the running build and with the revision's own
    // verdict on whether it had anything to compile (DEC-258). A read that judged it differently would
    // describe one page two ways.
    private PageImageState Describe(PageTemplate page) => PageImageStatus.Describe(
        page.Image?.RuntimeId,
        page.Image?.Bytes is not null,
        page.Image?.Diagnostics.Count > 0,
        pages.RuntimeId,
        page.TemplateHasCode);
}
