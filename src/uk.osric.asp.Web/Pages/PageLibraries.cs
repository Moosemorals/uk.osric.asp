// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>One live library, as a compile reads it.</summary>
/// <param name="Key">The library's canonical key.</param>
/// <param name="Hash">The head revision's hash, which a dependent's fingerprint records.</param>
/// <param name="Template">The library's source.</param>
internal sealed record PageLibrary(PagePath Key, string Hash, string Template);

/// <summary>One live library's identity, which is all a staleness check needs.</summary>
/// <param name="Key">The library's canonical key.</param>
/// <param name="Hash">The head revision's hash.</param>
internal sealed record PageLibraryHead(PagePath Key, string Hash);

/// <summary>The live libraries a page may name, read through the page read seam.</summary>
/// <remarks>
/// A compiler needs the libraries a page could reach, and a render needs to know whether the ones it
/// was compiled against have moved. Both answers come from the same listing, so the sources are only
/// read when something is actually going to compile.
/// </remarks>
internal sealed class PageLibraries(IPageReadStore read) {

    /// <summary>The identity of every live library, ordered by key.</summary>
    public async Task<IReadOnlyList<PageLibraryHead>> HeadsAsync(CancellationToken cancellationToken = default) {
        IReadOnlyList<PageReadHead> heads = await read.ListLibrariesAsync(cancellationToken);

        return [.. heads.Select(head => new PageLibraryHead(PagePath.Normalize(head.Key), head.Hash))];
    }

    /// <summary>Every live library and its source, ordered by key.</summary>
    public async Task<IReadOnlyList<PageLibrary>> LiveAsync(CancellationToken cancellationToken = default) {
        IReadOnlyList<PageReadHead> heads = await read.ListLibrariesAsync(cancellationToken);
        List<PageLibrary> libraries = [];
        libraries.EnsureCapacity(heads.Count);

        foreach (PageReadHead head in heads) {
            PageReadSource? source = await read.GetSourceAsync(head.Key, cancellationToken);
            if (source is not null) {
                libraries.Add(new PageLibrary(PagePath.Normalize(head.Key), head.Hash, source.Template));
            }
        }

        return libraries;
    }
}

/// <summary>The fingerprint a page's image records of the libraries it was compiled against.</summary>
internal static class PageLibraryFingerprint {

    /// <summary>Fingerprints the closure: one <c>key@hash</c> per library, in ordinal key order.</summary>
    /// <param name="libraries">The keys and the revision hashes they were compiled from.</param>
    /// <returns>
    /// The fingerprint, or an empty string when the page named no library. It is stored beside the
    /// image and compared with the heads, so a moved library makes exactly its dependents stale.
    /// </returns>
    public static string Of(IEnumerable<(string Key, string Hash)> libraries) {
        ArgumentNullException.ThrowIfNull(libraries);

        return string.Join('\n', libraries
            .OrderBy(library => library.Key, StringComparer.Ordinal)
            .Select(library => $"{library.Key}@{library.Hash}"));
    }
}
