// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a listing row is: a stored page, a tombstone, a site route or a folder.</summary>
/// <remarks>
/// <para>
/// It is a set of flags because a key can be more than one of them: <c>/blog</c> may hold a page and
/// have <c>/blog/post</c> below it, so it is <see cref="Page"/> and <see cref="Folder"/> in one row,
/// and <c>/admin</c> is a <see cref="Route"/> and a folder for the same reason.
/// </para>
/// <para>
/// A folder is not a stored thing. It exists because at least one key lives below it, and the host
/// works that out from the keys it read, so there is no folder row to keep in step with a save, a
/// move or a scrub.
/// </para>
/// </remarks>
[Flags]
public enum PageEntryKind {

    /// <summary>Nothing: an uninitialised value.</summary>
    None = 0,

    /// <summary>A head is stored at the key, live or a tombstone.</summary>
    Page = 1,

    /// <summary>The stored head is deleted and keeps its history; never set without <see cref="Page"/>.</summary>
    Tombstone = 2,

    /// <summary>The site's own routes answer the key, so no page is stored there.</summary>
    Route = 4,

    /// <summary>At least one key lives below this one.</summary>
    Folder = 8,
}

/// <summary>One row of a page listing: a key, what it is and, when a head is stored there, its facts.</summary>
/// <param name="Key">The row's canonical key.</param>
/// <param name="Kind">What the key is, as flags a row may combine.</param>
/// <remarks>
/// <para>
/// The marks are the host's own rules reported rather than derived: a view can filter a listing
/// without owning the reserved-path list, the content-type table or the revision verdict. A row
/// that is only a folder has no saved time, no hash, no state, no libraries and no seed, and the
/// nullable types say so rather than an empty string saying nothing.
/// </para>
/// <para>
/// <see cref="State"/> is what the head's image compiled to, in the host's words — "Compiled",
/// "Compiled with warnings", "Compile failed", "Stale", "Not compiled" or "Stored as source" — so a
/// page's listing says exactly what the shell's own listing says, without reading source or
/// inventing a second spelling. How a view marks it is the view's business: the shell's list has a
/// class of its own, and a page may use the words as an attribute or map them to its own class.
/// </para>
/// </remarks>
public readonly record struct PageEntry(string Key, PageEntryKind Kind) {

    /// <summary>When the head's revision was saved, or <see langword="null"/> when no head is stored.</summary>
    public DateTimeOffset? SavedAt { get; init; }

    /// <summary>The head's revision hash, or <see langword="null"/> when no head is stored.</summary>
    public string? Hash { get; init; }

    /// <summary>Whether the key is served as HTML; false for a row that is only a folder.</summary>
    public bool IsHtml { get; init; }

    /// <summary>Whether the head has no code, so its source is the response.</summary>
    public bool IsAsset { get; init; }

    /// <summary>
    /// Whether the head is a library: declarations only, compiled into the pages that name it and
    /// never served.
    /// </summary>
    public bool IsLibrary { get; init; }

    /// <summary>
    /// Whether the head is a worker: declarations that program a job a page starts, and never served.
    /// </summary>
    public bool IsWorker { get; init; }

    /// <summary>What the head's image compiled to, in the host's words; empty when no head is stored.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// The keys of the libraries this page was compiled against, transitively, or empty when it names
    /// none or holds no head.
    /// </summary>
    /// <remarks>
    /// This is what a save to one of those keys would recompile, so a view can mark a row before the
    /// edit rather than after it. It is the closure the compiler used rather than what the source
    /// spells: a library that names another library is in it, and so is one the compiler dropped as
    /// unreachable. A key stays in the list while the page has not recompiled, so a library that has
    /// been deleted is still named — <see cref="State"/> says what happened to the compile.
    /// </remarks>
    public IReadOnlyList<string> Libraries { get; init; } = [];

    /// <summary>
    /// What this page is relative to the seed the release ships, or <see langword="null"/> when the
    /// key has no seed either side of the store or holds no head.
    /// </summary>
    public PageSeedSummary? Seed { get; init; }

    /// <summary>Whether the stored head is deleted. Reads <see cref="Kind"/>.</summary>
    public bool IsDeleted => Kind.HasFlag(PageEntryKind.Tombstone);

    /// <summary>Whether the site's own routes answer the key. Reads <see cref="Kind"/>.</summary>
    public bool IsReserved => Kind.HasFlag(PageEntryKind.Route);
}
