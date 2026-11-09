// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>The page keys the site's own routes already own.</summary>
/// <remarks>
/// <para>
/// Entries are matched as prefixes, not exactly: <c>/edit/{**path}</c> shadows everything beneath it,
/// so a page called <c>/edit/anything</c> is as unreachable as one called <c>/edit</c>.
/// </para>
/// <para>
/// The list lives in configuration (<c>Pages:ReservedPaths</c>) because the routes belong to the
/// shell; <see cref="Default"/> is used whenever the section is missing or empty, so a configuration
/// mistake cannot silently switch the check off.
/// </para>
/// </remarks>
internal sealed class ReservedPaths {

    /// <summary>The keys reserved when none are configured.</summary>
    public static IReadOnlyList<string> Default { get; } =
        ["/admin/export", "/admin/import", "/assistant", "/edit", "/favicon.ico", "/heath", "/login", "/logout", "/metrics", "/status"];

    private readonly IReadOnlyList<PagePath> _keys;

    /// <summary>Creates the list from the configured keys.</summary>
    /// <param name="paths">The configured keys; <see cref="Default"/> is used when this is empty.</param>
    public ReservedPaths(IEnumerable<string>? paths) {
        List<PagePath> keys = [];

        foreach (string path in paths ?? []) {
            if (!string.IsNullOrWhiteSpace(path)) {
                keys.Add(PagePath.Normalize(path));
            }
        }

        _keys = keys.Count > 0 ? keys : [.. Default.Select(PagePath.Normalize)];
    }

    /// <summary>The reserved keys themselves, in configuration order, as canonical paths.</summary>
    public IReadOnlyList<PagePath> Keys => _keys;

    /// <summary>Whether the site's routes already answer for this key.</summary>
    /// <param name="path">The canonical key to test.</param>
    public bool IsReserved(PagePath path) => MatchingPrefix(path) is not null;

    /// <summary>The reserved key that owns this one, so a key beneath a route can name the route.</summary>
    /// <param name="path">The canonical key to test.</param>
    public PagePath? MatchingPrefix(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);

        foreach (PagePath prefix in _keys) {
            if (string.Equals(path.Key, prefix.Key, StringComparison.Ordinal)
                || path.Key.StartsWith(prefix.Key + "/", StringComparison.Ordinal)) {
                return prefix;
            }
        }

        return null;
    }
}
