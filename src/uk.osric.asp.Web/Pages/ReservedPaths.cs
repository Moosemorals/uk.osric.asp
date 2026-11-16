// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Shell;

/// <summary>The page keys the site's own routes already own.</summary>
/// <remarks>
/// <para>
/// Entries are matched as prefixes, not exactly: <c>/admin/scrub/{**path}</c> shadows everything
/// beneath it, so a page called <c>/admin/scrub/anything</c> is as unreachable as one called
/// <c>/admin/scrub</c>.
/// </para>
/// <para>
/// The list is the shell's own route table, not configuration: a route exists because the site's code
/// answers it, and a shorter list could only let a stored page shadow a route such as
/// <c>/login</c> (<c>shell-reserved-keys-are-an-invariant</c>).
/// </para>
/// </remarks>
internal sealed class ReservedPaths {

    /// <summary>The keys the shell's route table names.</summary>
    public static IReadOnlyList<string> Default { get; } = ShellCatalogue.Reserved;

    private readonly IReadOnlyList<PagePath> _keys;

    /// <summary>Creates the list from the configured keys.</summary>
    /// <param name="paths">The configured keys, which must hold at least one.</param>
    /// <exception cref="ArgumentException">No key was given, so no route would be reserved.</exception>
    public ReservedPaths(IEnumerable<string> paths) {
        ArgumentNullException.ThrowIfNull(paths);

        List<PagePath> keys = [];
        foreach (string path in paths) {
            if (!string.IsNullOrWhiteSpace(path)) {
                keys.Add(PagePath.Normalize(path));
            }
        }

        if (keys.Count == 0) {
            throw new ArgumentException("At least one reserved page key is required.", nameof(paths));
        }

        _keys = keys;
    }

    /// <summary>The reserved keys themselves, in the route table's order, as canonical paths.</summary>
    public IReadOnlyList<PagePath> Keys => _keys;

    /// <summary>Whether the site's routes already answer for this key.</summary>
    /// <param name="path">The canonical key to test.</param>
    public bool IsReserved(PagePath path) => MatchingPrefix(path) is not null;

    /// <summary>The reserved key that owns this one, so a key beneath a route can name the route.</summary>
    /// <param name="path">The canonical key to test.</param>
    /// <remarks>
    /// The most specific key wins, so a read beneath <c>/admin/scrub</c> names that route rather than
    /// the <c>/admin</c> root above it.
    /// </remarks>
    public PagePath? MatchingPrefix(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);

        PagePath? match = null;
        foreach (PagePath prefix in _keys) {
            bool matches = string.Equals(path.Key, prefix.Key, StringComparison.Ordinal)
                || path.Key.StartsWith(prefix.Key + "/", StringComparison.Ordinal);
            if (matches && (match is null || prefix.Key.Length > match.Key.Length)) {
                match = prefix;
            }
        }

        return match;
    }
}
