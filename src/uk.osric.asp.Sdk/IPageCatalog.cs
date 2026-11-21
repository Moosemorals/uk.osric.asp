// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A read-only view of the site's page store, and of the links the shell knows.</summary>
/// <remarks>
/// <para>
/// Four store reads, one list of shell links and no writes: a page can list the store's tree, read a
/// live page's template, read a page's history, read one retained revision and ask what the shell can
/// do.
/// It never meets an image, a symbol or a writable member; saving is a separate capability.
/// </para>
/// <para>
/// The read is as public as the page that calls it. There is no gate in the host, so an anonymous
/// visitor gets the same answers a signed-in one does, and a page that wants its listing or its
/// source private writes the ordinary guard:
/// <c>if (ctx.User is not { } user) { ctx.Response.RedirectToLogin(); return; }</c>. Because the read is not
/// gated, a page that calls <see cref="ReadAsync"/> or <see cref="ReadRevisionAsync"/> publishes the
/// source it reads to whoever can reach that page.
/// </para>
/// <para>
/// Every store read returns a <see cref="Task"/> and takes no cancellation token, as
/// <see cref="IPageStorage"/> and <see cref="IHttpFetch"/> do: the render owns
/// <see cref="IContext.Aborted"/> and the deadline, and the implementation links them per call. A
/// render that was abandoned, or a deadline that passed, throws when the returned task is awaited.
/// <see cref="ShellLinks"/> is a value rather than a read, and refuses the same way.
/// </para>
/// <para>
/// The read is bounded. One listing returns at most the host's row cap and refuses rather than
/// truncating, and every template the render pulls in shares one source allowance across the page
/// and its layouts, which is also a refusal rather than a truncated answer.
/// </para>
/// </remarks>
public interface IPageCatalog {

    /// <summary>The links the shell knows, as values a page may render.</summary>
    /// <returns>
    /// Every link the shell can draw, both the site menu and the administration menu, each with the
    /// section it belongs to and the audience it is for. The order is the shell's reading order.
    /// </returns>
    /// <remarks>
    /// <para>
    /// It answers what the shell can do, not what the store holds: the links name shell routes and
    /// the editor's configured key, so a page cannot build them itself. The host returns the whole
    /// list and the page decides — it filters on <see cref="ShellAudience"/> with its own
    /// <see cref="IContext.User"/>, picks the section, orders, relabels, marks the current one and
    /// writes its own markup.
    /// </para>
    /// <para>
    /// The sign-in link carries this request's way back, so a layout and the page it wraps see one
    /// answer. A worker's catalog has no request and no visitor, so it has no links.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The render was abandoned.</exception>
    public IReadOnlyList<ShellLink> ShellLinks();

    /// <summary>Lists the keys of a subtree, one slice at a time, each saying what it is.</summary>
    /// <param name="request">
    /// The subtree, its depth, the cursor to resume from, the limit to answer and the token to compare.
    /// </param>
    /// <returns>
    /// The keys under the prefix, each with its kind — a stored page, a tombstone, a site route or a
    /// folder, which combine — and the head's facts where a head is stored, ordered by key, whether
    /// more stand after the last one, the prefix's fingerprint and whether it differs from the
    /// request's token.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A slice is a keyset, not an offset: <see cref="PageListingRequest.After"/> is the last key the
    /// caller was given, so a page saved between two calls neither skips nor repeats a row. A cursor
    /// past the last key answers an empty page with <see cref="PageListing.Truncated"/> false.
    /// <see cref="PageListing.Version"/> is an opaque fingerprint of every head under the prefix,
    /// read with the rows; pass it back as <see cref="PageListingRequest.Version"/> on the next slice
    /// and <see cref="PageListing.Changed"/> says whether the list moved. It is a warning rather than
    /// a refusal, and it covers the head rows rather than what they compiled to.
    /// </para>
    /// <para>
    /// A folder is not stored: it is a key with at least one key below it, worked out by the host, and
    /// it is answered once, by the slice that first reaches a key under it. The site's own routes are
    /// rows too, so a page draws the tree the shell's own tools draw without owning the route table.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The prefix or the cursor cannot name a key, for example one containing <c>?</c> or <c>#</c>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The limit is smaller than one row or larger than the host's listing cap; the message names the
    /// setting to raise.
    /// </exception>
    /// <exception cref="InvalidOperationException">The render was abandoned.</exception>
    public Task<PageListing> ListAsync(PageListingRequest request);

    /// <summary>Reads a live page's template.</summary>
    /// <param name="key">The page's key; normalised before it is read.</param>
    /// <returns>
    /// The page's current source with its hash and saved time, or <see langword="null"/> when no
    /// live page has that key. A tombstone is listed as deleted, and reads as absent.
    /// </returns>
    /// <exception cref="ArgumentException">The key cannot name a page.</exception>
    /// <exception cref="InvalidOperationException">
    /// The render was abandoned, or the source would take the render past its source allowance.
    /// </exception>
    public Task<PageSource?> ReadAsync(string key);

    /// <summary>Lists one page's history, newest first.</summary>
    /// <param name="key">The page's key; normalised before it is read.</param>
    /// <returns>
    /// The page's revisions, including those of a tombstoned page: the history survives a delete by
    /// design. An unknown key is an empty list rather than an error.
    /// </returns>
    /// <exception cref="ArgumentException">The key cannot name a page.</exception>
    /// <exception cref="InvalidOperationException">The render was abandoned.</exception>
    public Task<IReadOnlyList<PageRevisionInfo>> RevisionsAsync(string key);

    /// <summary>Reads one retained revision's template.</summary>
    /// <param name="key">The page's key; normalised before it is read.</param>
    /// <param name="hash">The revision's hash, as <see cref="RevisionsAsync"/> reported it.</param>
    /// <returns>
    /// The revision's source with its hash and saved time, or <see langword="null"/> when that page
    /// has no revision with that hash. Both handles are required, so a hash remembered from one page
    /// cannot be spent on another.
    /// </returns>
    /// <exception cref="ArgumentException">The key cannot name a page, or the hash is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The render was abandoned, or the source would take the render past its source allowance.
    /// </exception>
    public Task<PageSource?> ReadRevisionAsync(string key, string hash);
}
