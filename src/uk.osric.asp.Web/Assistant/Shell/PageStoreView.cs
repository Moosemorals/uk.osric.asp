// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>The page store as the shell sees it: a tree of nodes, and what a node holds.</summary>
internal interface IPageStoreView {

    /// <summary>Every node of the tree, in key order: the keys that exist and the folders above them.</summary>
    /// <param name="cancellationToken">The run's token.</param>
    Task<IReadOnlyList<ShellPath>> NodesAsync(CancellationToken cancellationToken);

    /// <summary>The nodes a search may read, in key order.</summary>
    /// <param name="scopes">The paths to read: each in itself, and what is below it when the search descends.</param>
    /// <param name="recursive">Whether to read everything below the paths, or only what is at them.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<IReadOnlyList<ShellSource>> SourcesAsync(
        IReadOnlyList<ShellPath> scopes,
        bool recursive,
        CancellationToken cancellationToken);

    /// <summary>What a node holds, or null when nothing is stored at it.</summary>
    /// <param name="path">The node to read.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<ShellFile?> ReadAsync(ShellPath path, CancellationToken cancellationToken);
}

/// <summary>The store behind the shell, over the editor and the path policy.</summary>
/// <remarks>
/// Everything the shell needs to know about the store is here, once: which keys exist, which folders
/// exist because a key lives below them, what permission a node carries, what a node holds and how a
/// page compiles. An applet asks; it never reaches for the editor or the policy itself.
/// </remarks>
/// <param name="pages">The editor, which reads pages and knows the running build.</param>
/// <param name="paths">The policy, which says what exists and what may be written.</param>
internal sealed class PageStoreView(PageEditor pages, PagePathPolicy paths) : IPageStoreView {

    private static readonly PagePath s_root = PagePath.Normalize("/");

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ShellPath>> NodesAsync(CancellationToken cancellationToken) {
        List<PagePath> keys = [.. paths.Reserved];
        foreach (PageListing head in await pages.ListAsync(cancellationToken)) {
            if (!head.IsDeleted && !paths.IsReserved(head.Path)) {
                keys.Add(head.Path);
            }
        }

        SortedSet<string> nodes = new(StringComparer.Ordinal) { s_root.Key };
        foreach (PagePath key in keys) {
            _ = nodes.Add(key.Key);
            for (string? folder = ShellTree.FolderOf(key.Key); folder is not null; folder = ShellTree.FolderOf(folder)) {
                _ = nodes.Add(folder);
            }
        }

        HashSet<string> stored = [.. keys.Select(key => key.Key)];
        return [.. nodes.Select(node => {
            PagePath path = PagePath.Normalize(node);
            bool isFolder = nodes.Any(other => ShellTree.IsBelow(PagePath.Normalize(other), path));
            return new ShellPath(path, isFolder, stored.Contains(node), Mode(path, isFolder));
        })];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ShellSource>> SourcesAsync(
        IReadOnlyList<ShellPath> scopes,
        bool recursive,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(scopes);

        List<ShellPath> nodes = [.. await NodesAsync(cancellationToken)];
        List<ShellPath> named = scopes.Count == 0
            ? [.. nodes.Where(node => node.Key.Key == "/")]
            : [.. scopes];

        List<ShellSource> sources = [];
        foreach (ShellPath node in nodes) {
            if (!node.IsPage || !named.Any(scope => InScope(node, scope, recursive))) {
                continue;
            }

            ShellFile? file = await ReadAsync(node, cancellationToken);
            if (file is not null) {
                sources.Add(new ShellSource(node, file.Text));
            }
        }

        return sources;
    }

    // A path names a page as readily as a folder, so the key it names is always in scope; what lives
    // below it is in scope only when the search descends.
    private static bool InScope(ShellPath node, ShellPath scope, bool recursive) =>
        string.Equals(node.Key.Key, scope.Key.Key, StringComparison.Ordinal)
        || (recursive ? ShellTree.IsBelow(node.Key, scope.Key) : ShellTree.IsDirectlyBelow(node.Key, scope.Key));

    /// <inheritdoc/>
    public async Task<ShellFile?> ReadAsync(ShellPath path, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(path);

        if (!path.IsPage) {
            return null;
        }
        if (paths.IsReserved(path.Key)) {
            return new ShellFile(path, ShellRoutes.Text(path.Key), null, "shell file");
        }

        PageTemplate? page = await pages.GetAsync(path.Key, cancellationToken);
        return page is null
            ? null
            : new ShellFile(path, page.Template, page, PageImageStatus.Describe(
                page.Image?.RuntimeId,
                page.Image?.Bytes is not null,
                page.Image?.Diagnostics.Count > 0,
                pages.RuntimeId).Describe());
    }

    // The permission column: d for a folder, and w only where a save would be accepted. There is no
    // host file mode here.
    private string Mode(PagePath path, bool isFolder) => (isFolder, paths.CanWrite(path)) switch {
        (true, true) => "drwxr-xr-x",
        (true, false) => "dr-xr-xr-x",
        (false, true) => "-rw-r--r--",
        (false, false) => "-r--r--r--",
    };
}
