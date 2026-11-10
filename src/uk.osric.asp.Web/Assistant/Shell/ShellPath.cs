// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>One node of the page store as the shell sees it.</summary>
/// <param name="Key">The canonical key.</param>
/// <param name="IsFolder">Whether any key lives beneath it, so that it can be listed.</param>
/// <param name="IsPage">Whether a page or a shell file is stored at the key itself.</param>
/// <param name="Mode">The permission column: <c>d</c> for a folder, <c>-</c> for a page, and <c>w</c> where a save would be accepted.</param>
internal sealed record ShellPath(PagePath Key, bool IsFolder, bool IsPage, string Mode);

/// <summary>What a key holds when it is read.</summary>
/// <param name="Path">The node that was read.</param>
/// <param name="Text">Its text: a page's source, or a route's record.</param>
/// <param name="Page">The page, or null when the key is a shell file.</param>
/// <param name="Status">The compile state, as <c>stat</c> prints it.</param>
internal sealed record ShellFile(ShellPath Path, string Text, PageTemplate? Page, string Status);

/// <summary>A node a command may read, with the text it holds.</summary>
/// <param name="Path">The node.</param>
/// <param name="Text">Its text: a page's source, or a route's record.</param>
internal sealed record ShellSource(ShellPath Path, string Text);

/// <summary>The tree a path names: its nodes, and the rules for being inside one.</summary>
/// <remarks>
/// The store's keys form a tree, and the shell speaks of it as one. This is the only place the rules
/// live, so a listing, a search and a glob agree about what "below" means.
/// </remarks>
internal static class ShellTree {

    /// <summary>Whether a key lives anywhere below a path.</summary>
    /// <param name="key">The key to test.</param>
    /// <param name="folder">The path to test it against.</param>
    public static bool IsBelow(PagePath key, PagePath folder) => folder.Key == "/"
        ? key.Key.Length > 1
        : key.Key.StartsWith(folder.Key + "/", StringComparison.Ordinal);

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
    public static string? FolderOf(string key) {
        int separator = key.LastIndexOf('/');
        return separator <= 0 ? null : key[..separator];
    }
}
