// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>A canonical key for a stored page: an absolute, lowercase, slash-separated path.</summary>
/// <remarks>
/// <para><see cref="Normalize"/> applies these steps in order:</para>
/// <list type="number">
///   <item><description>A null, empty or whitespace-only path is the root, <c>/</c>.</description></item>
///   <item><description>The text is normalised to Unicode NFC, so a precomposed accent and a
///     combining accent give the same key.</description></item>
///   <item><description>The text is lowercased with the invariant culture, so <c>/Hello</c> and
///     <c>/hello</c> are the same key.</description></item>
///   <item><description>The text is split on <c>/</c> and empty segments are dropped, so leading,
///     trailing and repeated slashes collapse: <c>//hello//</c> becomes <c>/hello</c>.</description></item>
///   <item><description><c>.</c> segments are dropped.</description></item>
///   <item><description><c>..</c> removes the preceding segment, or is dropped when there is nothing
///     left to remove: <c>/a/../b</c> becomes <c>/b</c>, and <c>/..</c> becomes <c>/</c>.</description></item>
///   <item><description>A single <c>/</c> is prefixed, so every key is absolute.</description></item>
/// </list>
/// <para>
/// Names the routing layer reserves, such as <c>/edit</c> or <c>/error</c>, are not rejected here: the
/// shell owns the routes, so deciding what a page may not be called belongs there.
/// </para>
/// </remarks>
public sealed record PagePath {

    private readonly string _path;

    private PagePath(string path) {
        _path = path;
    }

    /// <summary>The canonical key: absolute, lowercase, and never ending with <c>/</c> unless it is
    /// the root.</summary>
    public string Path => _path;

    /// <summary>Normalises text into the canonical key for the page it names.</summary>
    /// <param name="path">The text to normalise, typically a <c>path</c> route value.</param>
    /// <returns>The canonical key, which is <c>/</c> when <paramref name="path"/> is null, empty or
    /// whitespace.</returns>
    /// <remarks>The steps are listed on <see cref="PagePath"/>.</remarks>
    public static PagePath Normalize(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return new PagePath("/");
        }

        string[] segments = path.Normalize().ToLowerInvariant().Split('/');

        List<string> normalized = [];

        foreach (string segment in segments) {
            if (segment is "" or ".") {
                continue;
            }

            if (segment is "..") {
                if (normalized.Count > 0) {
                    normalized.RemoveAt(normalized.Count - 1);
                }
                continue;
            }

            normalized.Add(segment);
        }

        return new PagePath('/' + string.Join('/', normalized));
    }

    public override string ToString() => Path;
}
