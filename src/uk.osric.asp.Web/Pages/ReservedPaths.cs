// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core;

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
    public static IReadOnlyList<string> Default { get; } = ["/edit", "/site.css", "/favicon.ico", "/heath", "/login", "/logout"];

    private readonly IReadOnlyList<string> _prefixes;

    /// <summary>Creates the list from the configured keys.</summary>
    /// <param name="paths">The configured keys; <see cref="Default"/> is used when this is empty.</param>
    public ReservedPaths(IEnumerable<string>? paths) {
        List<string> prefixes = [];

        foreach (string path in paths ?? []) {
            if (!string.IsNullOrWhiteSpace(path)) {
                prefixes.Add(PagePath.Normalize(path).Path);
            }
        }

        _prefixes = prefixes.Count > 0 ? prefixes : Default;
    }

    /// <summary>Whether the site's routes already answer for this key.</summary>
    /// <param name="path">The canonical key to test.</param>
    public bool IsReserved(PagePath path) {
        ArgumentNullException.ThrowIfNull(path);

        foreach (string prefix in _prefixes) {
            if (string.Equals(path.Path, prefix, StringComparison.Ordinal)
                || path.Path.StartsWith(prefix + "/", StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
