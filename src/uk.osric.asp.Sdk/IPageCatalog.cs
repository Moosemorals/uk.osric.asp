// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A read-only view of the site's page store.</summary>
/// <remarks>
/// <para>
/// Four reads and no writes: a page can list the store, read a live page's template, read a page's
/// history and read one retained revision. It never meets an image, a symbol or a writable member;
/// saving is a separate capability.
/// </para>
/// <para>
/// The read is as public as the page that calls it. There is no gate in the host, so an anonymous
/// visitor gets the same answers a signed-in one does, and a page that wants its listing or its
/// source private writes the ordinary guard:
/// <c>if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; }</c>. Because the read is not
/// gated, a page that calls <see cref="ReadAsync"/> or <see cref="ReadRevisionAsync"/> publishes the
/// source it reads to whoever can reach that page.
/// </para>
/// <para>
/// Every member returns a <see cref="Task"/> and takes no cancellation token, as
/// <see cref="IPageStorage"/> and <see cref="IHttpFetch"/> do: the render owns
/// <see cref="IContext.Aborted"/> and the deadline, and the implementation links them per call. A
/// render that was abandoned, or a deadline that passed, throws when the returned task is awaited.
/// </para>
/// <para>
/// The read is bounded. One listing returns at most the host's row cap and refuses rather than
/// truncating, and every template the render pulls in shares one source allowance across the page
/// and its layouts, which is also a refusal rather than a truncated answer.
/// </para>
/// </remarks>
public interface IPageCatalog {

    /// <summary>Lists the heads of the store, under a prefix.</summary>
    /// <param name="prefix">
    /// The subtree to list. <c>""</c> and <c>"/"</c> are the whole store; any other value is
    /// normalised as a page key, so <c>/Blog/</c> and <c>/blog</c> are the same request. A prefix
    /// lists the key itself and the keys beneath it, never a key that merely starts with the same
    /// text: <c>/blog</c> lists <c>/blog</c> and <c>/blog/post</c>, never <c>/blogroll</c>.
    /// </param>
    /// <returns>
    /// Every head under the prefix, including tombstones, seeded assets and reserved keys, ordered
    /// by key and marked so a view can pick what it wants.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The prefix cannot name a key, for example one containing <c>?</c> or <c>#</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The render was abandoned, or the prefix holds more rows than the host's listing cap; the
    /// message names the cap and suggests a prefix.
    /// </exception>
    public Task<IReadOnlyList<PageEntry>> ListAsync(string prefix);

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
