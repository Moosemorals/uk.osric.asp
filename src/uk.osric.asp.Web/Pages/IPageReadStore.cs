// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>One row of a page listing, as the read store answers it.</summary>
/// <param name="Key">The page's canonical key.</param>
/// <param name="SavedAt">When the head's revision was saved.</param>
/// <param name="Hash">The head's revision hash.</param>
/// <param name="IsDeleted">Whether the page is a tombstone.</param>
/// <param name="TemplateKind">
/// What the head's template is for, or <see langword="null"/> when it has never been judged.
/// </param>
/// <param name="RuntimeId">The build that compiled the head, or <see langword="null"/> when it has no image.</param>
/// <param name="HasImage">Whether the head has a stored image.</param>
/// <param name="HasDiagnostics">Whether the head's stored compile has diagnostics.</param>
/// <param name="SeedHash">The seed the page's content is anchored to, or null when unknown.</param>
/// <param name="ContentHash">The content hash of the head's stored source.</param>
/// <param name="TemplateLibraries">
/// The fingerprint of the libraries the head was compiled against, or <see langword="null"/> when it
/// named none.
/// </param>
internal sealed record PageReadHead(
    string Key,
    DateTimeOffset SavedAt,
    string Hash,
    bool IsDeleted,
    PageKind? TemplateKind = null,
    string? RuntimeId = null,
    bool HasImage = false,
    bool HasDiagnostics = false,
    string? SeedHash = null,
    string? ContentHash = null,
    string? TemplateLibraries = null);

/// <summary>One slice of a listing, and the fingerprint of every head under its prefix.</summary>
/// <param name="Rows">The heads the slice selected, in key order.</param>
/// <param name="Version">The fingerprint of the whole prefix, read in the same snapshot as the rows.</param>
internal sealed record PageReadSlice(IReadOnlyList<PageReadHead> Rows, string Version);

/// <summary>One template the read store retrieved.</summary>
/// <param name="Key">The page's canonical key.</param>
/// <param name="Hash">The revision's hash.</param>
/// <param name="SavedAt">When the revision was saved.</param>
/// <param name="Template">The stored template text.</param>
internal sealed record PageReadSource(string Key, string Hash, DateTimeOffset SavedAt, string Template);

/// <summary>One row of a page's history, as the read store answers it.</summary>
/// <param name="Hash">The revision's hash.</param>
/// <param name="SavedAt">When the revision was saved.</param>
/// <param name="IsCurrent">Whether the live head points at this revision.</param>
internal sealed record PageReadRevision(string Hash, DateTimeOffset SavedAt, bool IsCurrent);

/// <summary>The read-only seam a page's catalog reads through.</summary>
/// <remarks>
/// <para>
/// It is a seam of its own rather than <see cref="IPageStore"/> because that one is scoped to the
/// request's <c>StorageDbContext</c>, and a page's task deliberately outlives the request. Every
/// call here owns its context, as <see cref="IPageStateStore"/> does.
/// </para>
/// <para>
/// It answers with small records and never with a <c>PagePath</c>: normalising a key and deciding
/// what a listing reports is the capability's job, so a page never meets a core type. There is no
/// write member, and no member that names an image, a symbol or an archive.
/// </para>
/// <para>
/// A listing reads its rows and the prefix's fingerprint together, in one deferred read
/// transaction, so the token names the snapshot the rows came from. Nothing is written and nothing
/// is pinned beyond the two statements.
/// </para>
/// </remarks>
internal interface IPageReadStore {

    /// <summary>Lists the live library pages, ordered by key.</summary>
    /// <remarks>
    /// This is the compiler's read, not a page's: a library is an input to every page that names it,
    /// so a row cap here would stop those pages compiling rather than shorten a listing.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every live library head, in ordinal key order.</returns>
    Task<IReadOnlyList<PageReadHead>> ListLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the heads under a prefix, ordered by key, from a cursor, and fingerprints the prefix.</summary>
    /// <param name="prefix">The canonical subtree prefix; <c>/</c> is the whole store.</param>
    /// <param name="after">The canonical key to resume after, or <see langword="null"/> for the first row.</param>
    /// <param name="limit">The most rows to select, as a limit in the statement.</param>
    /// <param name="recursive">
    /// Whether to select the whole subtree or only the prefix and its direct children, which is what a
    /// listing that descends one folder at a time asks for.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// At most <paramref name="limit"/> heads with a key past the cursor, in ordinal key order, and the
    /// fingerprint of every head under the prefix. The two are read in one snapshot, so the version
    /// names the rows it came from rather than a later store. The fingerprint covers the whole prefix
    /// whatever the depth, because a one-level answer is still a slice of that prefix's list.
    /// </returns>
    Task<PageReadSlice> ListAsync(
        string prefix, string? after, int limit, bool recursive = true, CancellationToken cancellationToken = default);

    /// <summary>Lists the direct children of a prefix that hold something below them.</summary>
    /// <param name="prefix">The canonical prefix; <c>/</c> is the whole store.</param>
    /// <param name="after">The canonical key to resume after, or <see langword="null"/> for the first.</param>
    /// <param name="limit">The most folder keys to select, as a limit in the statement.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The direct children of the prefix with at least one key below them — a tombstone's included —
    /// in ordinal key order and past the cursor.
    /// </returns>
    /// <remarks>
    /// It exists for the listing that answers one level: a child whose only content is deeper is not
    /// a head the one-level read can see, and leaving it out would be a tree missing a branch. A
    /// recursive listing derives its folders from the keys its slice already carries, so it never
    /// asks this and never pays for a second statement.
    /// </remarks>
    Task<IReadOnlyList<string>> ListFoldersAsync(
        string prefix, string? after, int limit, CancellationToken cancellationToken = default);

    /// <summary>Fingerprints every head under a prefix without reading the heads themselves.</summary>
    /// <param name="prefix">The canonical subtree prefix; <c>/</c> is the whole store.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The fingerprint of every head under the prefix, a tombstone's included.</returns>
    /// <remarks>
    /// It is for a caller that reads sources rather than heads: the token taken before a walk and
    /// again at each slice says whether the scope moved under it.
    /// </remarks>
    Task<string> VersionAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Reads one head, which is how a caller learns what a key holds.</summary>
    /// <param name="key">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The head, a tombstone's included, or <see langword="null"/> when no page has that key.</returns>
    /// <remarks>
    /// It is one row rather than a listing of the key's subtree: a caller that needs a page's kind
    /// before it can decide anything must not be given its children as a side effect.
    /// </remarks>
    Task<PageReadHead?> GetHeadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Reads a live page's template.</summary>
    /// <param name="key">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The template, or <see langword="null"/> when no live page has that key.</returns>
    Task<PageReadSource?> GetSourceAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Lists one page's history, newest first.</summary>
    /// <param name="key">The canonical key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page's revisions, including those of a tombstone; empty for an unknown key.</returns>
    Task<IReadOnlyList<PageReadRevision>> ListRevisionsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Reads one revision's template, by both of its handles.</summary>
    /// <param name="key">The canonical key the revision belongs to.</param>
    /// <param name="hash">The revision's hash.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The template, or <see langword="null"/> when that page has no such revision.</returns>
    Task<PageReadSource?> GetRevisionSourceAsync(string key, string hash, CancellationToken cancellationToken = default);
}
