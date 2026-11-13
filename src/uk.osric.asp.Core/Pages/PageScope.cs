// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>One question a search asks of the tree: which keys it may read.</summary>
/// <param name="Path">The path the search starts at.</param>
/// <param name="IncludeSelf">Whether the path itself is read, as well as what is below it.</param>
/// <param name="Recursive">Whether everything below the path is read, or only what is directly below.</param>
/// <remarks>
/// A path names a page as readily as a folder, so a search usually includes it; it is left out when a
/// caller wants to read a folder's entries and not the folder's own key. The root is special only in
/// that everything but the root is below it.
/// </remarks>
public readonly record struct PageScope(PagePath Path, bool IncludeSelf, bool Recursive) {

    /// <summary>Whether a key is inside this scope.</summary>
    /// <param name="key">The key to test.</param>
    public bool Contains(PagePath key) {
        ArgumentNullException.ThrowIfNull(key);

        if (IncludeSelf && string.Equals(key.Key, Path.Key, StringComparison.Ordinal)) {
            return true;
        }

        return Recursive ? PageTree.IsBelow(key, Path) : PageTree.IsDirectlyBelow(key, Path);
    }
}
