// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Writes the site's page store: this page's own source, and any page the visitor may edit.</summary>
/// <remarks>
/// <para>
/// The host offers this to every content layer and never to a layout, because a layout is markup and
/// one that saved itself would append a revision on every page it wraps
/// (`pages-layouts-are-markup`). <strong>The authority is the signed-in visitor's, not the page's.</strong>
/// An anonymous render is refused with a <see cref="UserFacingException"/> before anything is compiled
/// or stored; which page asked is not part of the check.
/// </para>
/// <para>
/// There are two saves. <see cref="SaveAsync(string)"/> stores this page's own source, bound to
/// <see cref="IContext.Path"/>, and answers <see cref="PageSaveResult"/>; it is a page repairing itself.
/// <see cref="SaveAsync(string, string, string?)"/> stores any key the site's own routes do not answer,
/// conditional on the head hash the form was rendered from, and answers <see cref="PageEditResult"/>.
/// Both end in the same compile-then-store, so neither can answer differently about the same template.
/// A restore, a seed install and a seed adopt are saves too: nothing is rewound, a tombstone comes back
/// with its history, and one that would change nothing answers <see cref="PageEditOutcome.Unchanged"/>.
/// </para>
/// <para>
/// A <c>HEAD</c>, an ended or timed-out render, a second write in one render, an unaddressable key and a
/// reserved key are page bugs and throw <see cref="InvalidOperationException"/>; a key that names
/// nothing is the page's own answer through <see cref="Resolve"/>, not an exception. The read members —
/// <see cref="Resolve"/> and <see cref="SeedSourceAsync"/> — need no visitor and spend no action.
/// </para>
/// <para>
/// A write belongs to the next request: the running image is already loaded, so the response in hand is
/// still the page as it was. A write that has begun is never abandoned — the render's deadline bounds
/// the wait for a compile slot, not the author's edit (`pages-a-page-may-save-itself`).
/// </para>
/// <para>
/// Each member here is one write, stored when it answers, and it is that write's own transaction of
/// one — never part of a transaction the render happens to have open. A page that needs several writes
/// to become the store's state together opens an <see cref="IPageTransaction"/> with
/// <see cref="BeginTransaction"/>, stages the same members on it and commits once; a render may commit
/// as many transactions as it has work for.
/// </para>
/// </remarks>
public interface IPageWrites {

    /// <summary>Opens a transaction for several writes that become the store's state together.</summary>
    /// <returns>The transaction, which writes nothing unless <see cref="IPageTransaction.CommitAsync"/> is called.</returns>
    /// <remarks>
    /// A page holds one open at a time, and a transaction is not nestable: the store has one
    /// transaction and no second model. Opening one spends none of the render's writes, so a page may
    /// commit one transaction and open another.
    /// </remarks>
    /// <exception cref="UserFacingException">This render has no signed-in visitor, so nothing can be written.</exception>
    /// <exception cref="InvalidOperationException">
    /// The request is a <c>HEAD</c>, the render was abandoned or its deadline passed, or a transaction
    /// is already open.
    /// </exception>
    IPageTransaction BeginTransaction();

    /// <summary>Normalises a key and reports whether the site's own routes answer for it.</summary>
    /// <param name="key">The text the page was given.</param>
    /// <returns>The canonical key, or why the text names no page.</returns>
    PageKeyResolution Resolve(string key);

    /// <summary>Reads the source this release ships for a seed.</summary>
    /// <param name="key">The page's key; normalised before it is read.</param>
    /// <returns>The bundled source, or <see langword="null"/> when the release ships no seed for that key.</returns>
    /// <remarks>
    /// A read, so it needs no signed-in visitor and is not the render's one action: a confirmation shows
    /// the diff between this and the live source, which is the one thing a page cannot read for itself.
    /// Reading it never grants a write — <see cref="InstallSeedAsync"/> decides that.
    /// </remarks>
    /// <exception cref="ArgumentException">The key cannot name a page.</exception>
    Task<string?> SeedSourceAsync(string key);

    /// <summary>Saves this page's own source as a new revision.</summary>
    /// <param name="template">The template text. It may be empty; an empty page is a page.</param>
    /// <returns>The new revision's hash and the compiler's author-facing diagnostics.</returns>
    /// <remarks>
    /// The bound save, for the page that repairs itself: one call, no key to get wrong, and the answer
    /// a page shows about itself. It is unconditional, so a page that wants the stale check names its own
    /// key through the other overload and passes its head hash.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The template is <see langword="null"/>.</exception>
    /// <exception cref="UserFacingException">This render has no signed-in visitor, so nothing was saved.</exception>
    /// <exception cref="InvalidOperationException">
    /// The request is a <c>HEAD</c>, the render was abandoned or its deadline passed before the save
    /// started, or this render has already written.
    /// </exception>
    Task<PageSaveResult> SaveAsync(string template);

    /// <summary>Saves a page, only while its head still has the expected revision.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="template">The template text.</param>
    /// <param name="expectedHash">
    /// The head hash the form was rendered from, or <see langword="null"/> when the page had no live
    /// source: a new page, or a tombstone. A null expectation is refused as
    /// <see cref="PageEditOutcome.Stale"/> if the page exists by the time the save runs, so a create
    /// cannot quietly overwrite one.
    /// </param>
    /// <exception cref="UserFacingException">This render has no signed-in visitor, so nothing was saved.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a <c>HEAD</c>, an ended render or a second write.</exception>
    Task<PageEditResult> SaveAsync(string key, string template, string? expectedHash);

    /// <summary>Records a seed as installed without storing a revision.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <returns>
    /// Null when the key is not a seed; the outcome is <see cref="PageEditOutcome.Unchanged"/> when
    /// the record already names the shipped seed.
    /// </returns>
    /// <remarks>
    /// The answer to <see cref="PageSeedSummary.CanAdopt"/>: the page already holds the bytes this
    /// release ships, so recording it as installed changes the record and nothing else. A page whose
    /// content no longer matches the seed is a page bug, and throws.
    /// </remarks>
    /// <exception cref="UserFacingException">This render has no signed-in visitor.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a <c>HEAD</c>, an ended render, a second write, or content that is not the seed.</exception>
    Task<PageEditResult?> AdoptSeedAsync(string key);

    /// <summary>Tombstones a page, keeping its history.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <exception cref="UserFacingException">This render has no signed-in visitor.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a <c>HEAD</c>, an ended render or a second write.</exception>
    Task<PageDeleteResult> DeleteAsync(string key);

    /// <summary>Moves a page, its history and its state to another key.</summary>
    /// <param name="from">The key the page has; normalised and checked.</param>
    /// <param name="to">The key it should have; normalised and checked.</param>
    /// <param name="expectedHash">The head hash the caller read, so a move never applies to a page that changed underneath it.</param>
    /// <returns>
    /// Moved, or the reason it did not move. A move onto a tombstoned key clears the tombstone and
    /// keeps the history that key retained; a move onto a live page is
    /// <see cref="PageMoveOutcome.TargetExists"/> and writes nothing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Every revision moves and keeps the hash it was saved with, so the page's history is one
    /// history under its new name. Nothing is left at the old key: it answers as a key that never had
    /// a page, and a versioned link that named a revision answers at the new key or not at all.
    /// Nothing is compiled — the page serves the image it already had.
    /// </para>
    /// <para>
    /// The rules: a reserved key at either end, a page moving itself, and a library are page bugs and
    /// throw; the same key, a stale hash, a missing source, an occupied target and a key with pages
    /// below it are answers. A move spends the render's one action, like every other write.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A key cannot name a page.</exception>
    /// <exception cref="UserFacingException">This render has no signed-in visitor, so nothing was moved.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a library, this page's own key, a <c>HEAD</c>, an ended render or a second write.</exception>
    Task<PageMoveResult> MoveAsync(string from, string to, string expectedHash);

    /// <summary>Saves a retained revision's source as a new revision.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="hash">The retained revision's hash, as the catalog reported it.</param>
    /// <param name="expectedHash">The live head the form was rendered from, or null for a tombstone.</param>
    /// <returns>Null when that page has no revision with that hash.</returns>
    /// <exception cref="UserFacingException">This render has no signed-in visitor.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a <c>HEAD</c>, an ended render or a second write.</exception>
    Task<PageEditResult?> RestoreRevisionAsync(string key, string hash, string? expectedHash);

    /// <summary>Saves a page the release ships with from its bundled source.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="expectedHash">The live head the form was rendered from, or null for a tombstone.</param>
    /// <returns>Null when the key is not a seed.</returns>
    /// <exception cref="UserFacingException">This render has no signed-in visitor.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a <c>HEAD</c>, an ended render or a second write.</exception>
    Task<PageEditResult?> InstallSeedAsync(string key, string? expectedHash);
}
