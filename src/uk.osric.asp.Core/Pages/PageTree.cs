// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>The page store's keys as the tree they form.</summary>
/// <remarks>
/// A key is a path, so the store is a tree and "below", "directly below" and "the folder above" are
/// one rule each. The assistant's tools and its virtual shell both read the store and both speak of
/// it as a tree, so the rule lives here rather than in either of them.
/// </remarks>
public static class PageTree {

    /// <summary>Whether a key lives anywhere below a path.</summary>
    /// <param name="key">The key to test.</param>
    /// <param name="folder">The path to test it against.</param>
    /// <returns>
    /// True for a key under the folder at any depth. A folder is not below itself, and a prefix that is
    /// not a whole segment (<c>/ab</c> for <c>/a</c>) is another key. Every key but the root is below
    /// the root.
    /// </returns>
    public static bool IsBelow(PagePath key, PagePath folder) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(folder);

        return folder.Key == "/"
            ? key.Key.Length > 1
            : key.Key.StartsWith(folder.Key + "/", StringComparison.Ordinal);
    }

    /// <summary>Whether a key is an entry of a path: below it, and no deeper than one segment.</summary>
    /// <param name="key">The key to test.</param>
    /// <param name="folder">The path to test it against.</param>
    public static bool IsDirectlyBelow(PagePath key, PagePath folder) {
        if (!IsBelow(key, folder)) {
            return false;
        }

        string rest = folder.Key == "/" ? key.Key[1..] : key.Key[(folder.Key.Length + 1)..];
        return !rest.Contains('/');
    }

    /// <summary>The folder a key lives in, or null when that folder is the root.</summary>
    /// <param name="key">A canonical key.</param>
    /// <returns>The key's parent, or null for a key directly under the root and for the root itself.</returns>
    public static string? FolderOf(string key) {
        ArgumentNullException.ThrowIfNull(key);

        int separator = key.LastIndexOf('/');
        return separator <= 0 ? null : key[..separator];
    }

    /// <summary>Every folder above a key, outermost first.</summary>
    /// <param name="key">A canonical key.</param>
    /// <returns>
    /// The folders a listing has to show for the key to be reachable, such as <c>/a</c> and <c>/a/b</c>
    /// for <c>/a/b/c</c>. A key directly under the root has none, because the root is always there.
    /// </returns>
    public static IReadOnlyList<string> FoldersAbove(string key) {
        ArgumentNullException.ThrowIfNull(key);

        List<string> folders = [];
        for (string? folder = FolderOf(key); folder is not null; folder = FolderOf(folder)) {
            folders.Add(folder);
        }

        folders.Reverse();
        return folders;
    }
}
