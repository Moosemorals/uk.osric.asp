// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>One key a listing may answer, and whether anything lives below it.</summary>
/// <param name="Key">The key, which is canonical.</param>
/// <param name="IsFolder">
/// Whether the caller knows a key lives below this one. A read that answered one level can know it
/// without seeing the key below; a recursive read leaves it false and the fold works it out.
/// </param>
public readonly record struct PageListingKey(string Key, bool IsFolder);

/// <summary>The keys a listing answers, in ordinal order, and whether more stand after them.</summary>
/// <param name="Keys">The keys, each marked as a folder or not.</param>
/// <param name="Truncated">Whether at least one key stands after the last one.</param>
public sealed record PageListingKeys(IReadOnlyList<PageListingKey> Keys, bool Truncated);

/// <summary>The keys a listing answers, which is the tree its caller walks.</summary>
/// <remarks>
/// <para>
/// It folds the keys a read answered into the rows a page may draw: every key, plus every folder
/// above it, in ordinal order and past the cursor. A folder is never stored — it exists because a key
/// lives below it — so it is derived here rather than read, and the rule it uses is
/// <see cref="PageTree"/>'s.
/// </para>
/// <para>
/// The answer is one ordered stream, which is what makes the cursor a key: the slice that first
/// reaches a key below a folder is the slice that answers the folder, no earlier slice answers it
/// and no later one repeats it. A walk therefore visits every key once, and a key that is both a page
/// and a folder is one row rather than two.
/// </para>
/// </remarks>
public static class PageListingFold {

    /// <summary>Folds the keys a read answered into the keys the listing answers.</summary>
    /// <param name="facts">
    /// Every key the slice can see: the heads and routes it read, and the folders a one-level read
    /// named. Keys are canonical and need not be ordered.
    /// </param>
    /// <param name="prefix">The subtree being listed, which is a canonical key.</param>
    /// <param name="after">The cursor, exclusive, or <see langword="null"/> to start at the beginning.</param>
    /// <param name="recursive">Whether to answer every key below the prefix or only its own children.</param>
    /// <param name="limit">The most keys to answer.</param>
    /// <returns>The keys, in ordinal order, and whether more stand after the last one.</returns>
    /// <exception cref="ArgumentNullException">
    /// A fact, a fact's key or the prefix is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="limit"/> is less than one. The host's cap is the caller's to enforce, because
    /// it is configuration and this is a pure rule.
    /// </exception>
    public static PageListingKeys Of(
        IReadOnlyList<PageListingKey> facts,
        string prefix,
        string? after,
        bool recursive,
        int limit) {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        PagePath at = PagePath.Normalize(prefix);
        PagePath? cursor = after is null ? null : PagePath.Normalize(after);

        SortedSet<string> folders = [with(StringComparer.Ordinal)];
        foreach (PageListingKey fact in facts) {
            ArgumentNullException.ThrowIfNull(fact.Key);
            if (fact.IsFolder) {
                _ = folders.Add(fact.Key);
            }

            foreach (string folder in PageTree.FoldersAbove(fact.Key)) {
                _ = folders.Add(folder);
            }
        }

        SortedSet<string> nodes = [with(StringComparer.Ordinal)];
        foreach (PageListingKey fact in facts) {
            if (Wanted(fact.Key)) {
                _ = nodes.Add(fact.Key);
            }
        }

        // A folder is a row in its own right when the read named it; the prefix is never its own
        // child, though it is still a folder when something lives below it.
        foreach (string folder in folders) {
            if (!string.Equals(folder, at.Key, StringComparison.Ordinal) && Wanted(folder)) {
                _ = nodes.Add(folder);
            }
        }

        List<PageListingKey> keys = [];
        foreach (string key in nodes) {
            if (keys.Count == limit) {
                break;
            }

            keys.Add(new PageListingKey(key, folders.Contains(key)));
        }

        return new PageListingKeys(keys, nodes.Count > keys.Count);

        // The prefix itself is answered when it is a fact — the store's predicate has always included
        // it — and a key below it is answered at any depth, or only one level down when the caller
        // asked for one level.
        bool Wanted(string key) {
            if (cursor is not null && string.CompareOrdinal(key, cursor.Key) <= 0) {
                return false;
            }

            PagePath path = PagePath.Normalize(key);
            bool itself = string.Equals(path.Key, at.Key, StringComparison.Ordinal);
            if (!itself && !path.IsBelow(at)) {
                return false;
            }

            return recursive || itself || path.IsDirectlyBelow(at);
        }
    }
}
