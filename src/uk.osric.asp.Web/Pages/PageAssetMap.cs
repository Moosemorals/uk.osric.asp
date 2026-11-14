// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>The live assets one request may link by version, loaded at most once per request.</summary>
/// <remarks>
/// <para>
/// The map is the live heads whose verdict is <c>false</c>, keyed by a page key and holding the
/// revision hash. It is read once per request: a page chooses its sheets while it
/// runs, so the shell cannot know the set in advance, and <c>ctx.AssetUrl</c> cannot be asynchronous.
/// </para>
/// <para>
/// A stale entry is harmless. The hash still names a retained revision, so an older URL still serves
/// the older bytes; the next request reads the newer head.
/// </para>
/// </remarks>
internal static class PageAssetMap {

    private static readonly IReadOnlyDictionary<string, string> s_empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly object s_itemKey = new();

    /// <summary>The map a render with no readable store links against: every link stays as written.</summary>
    public static IReadOnlyDictionary<string, string> Empty => s_empty;

    /// <summary>Reads the map once for a request.</summary>
    /// <param name="context">The request whose items cache the map.</param>
    /// <param name="store">Where the heads are read.</param>
    /// <param name="limit">The row cap, from <c>Pages:Read</c>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<IReadOnlyDictionary<string, string>> ForRequestAsync(
        HttpContext context,
        IPageReadStore store,
        int limit,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if (context.Items.TryGetValue(s_itemKey, out object? cached)
            && cached is IReadOnlyDictionary<string, string> map) {
            return map;
        }

        IReadOnlyDictionary<string, string> loaded = s_empty;
        try {
            loaded = await LoadAsync(store, limit, cancellationToken);
        } catch (Exception exception) when (exception is not OperationCanceledException) {
            // Linking a bare URL is the fallback that still works: a failed lookup must not turn a
            // page that would otherwise render into an error page.
            context.RequestServices?.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(PageAssetMap).FullName!)
                .LogWarning(exception, "Could not read the asset map; links will be unversioned");
        }

        context.Items[s_itemKey] = loaded;
        return loaded;
    }

    /// <summary>Reads the live, no-code heads.</summary>
    /// <param name="store">Where the heads are read.</param>
    /// <param name="limit">The most rows to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(
        IPageReadStore store,
        int limit,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        IReadOnlyList<PageReadHead> heads = await store.ListAsync("/", limit, cancellationToken);

        Dictionary<string, string> map = [with(StringComparer.Ordinal)];
        foreach (PageReadHead head in heads) {
            if (head.IsDeleted) {
                continue;
            }

            if (PageAssetPolicy.IsAsset(head.TemplateKind)) {
                map[PagePath.Normalize(head.Key).Key] = head.Hash;
            }
        }

        return map;
    }

    /// <summary>Adds the revision hash to a site-relative link when its page is a live asset.</summary>
    /// <param name="map">The request's asset map.</param>
    /// <param name="href">The link as the page or view wrote it.</param>
    /// <returns>The versioned URL, or the link unchanged.</returns>
    /// <remarks>
    /// Only a bare site-relative path is touched. A query, a fragment, an absolute or
    /// protocol-relative URL, and any key that is not a live asset are all returned as written.
    /// </remarks>
    public static string Resolve(IReadOnlyDictionary<string, string> map, string href) {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(href);

        if (!PageDocument.IsSiteRelative(href)
            || href.Contains('?', StringComparison.Ordinal)
            || href.Contains('#', StringComparison.Ordinal)) {
            return href;
        }

        PagePath path = PagePath.Normalize(href);
        if (!path.IsAddressable || !string.Equals(path.Key, href, StringComparison.Ordinal)) {
            return href;
        }

        return map.TryGetValue(path.Key, out string? hash)
            ? $"{href}?v={hash}"
            : href;
    }
}
