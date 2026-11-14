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
internal sealed record PageReadHead(
    string Key,
    DateTimeOffset SavedAt,
    string Hash,
    bool IsDeleted,
    PageKind? TemplateKind = null,
    string? RuntimeId = null,
    bool HasImage = false,
    bool HasDiagnostics = false);

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

    /// <summary>Lists the heads under a prefix, ordered by key.</summary>
    /// <param name="prefix">The canonical subtree prefix; <c>/</c> is the whole store.</param>
    /// <param name="limit">The most rows to select, as a limit in the statement.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>At most <paramref name="limit"/> heads, in ordinal key order.</returns>
    Task<IReadOnlyList<PageReadHead>> ListAsync(string prefix, int limit, CancellationToken cancellationToken = default);

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
