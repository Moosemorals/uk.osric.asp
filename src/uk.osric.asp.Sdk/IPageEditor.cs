// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Edits any page in the site's store, for the one page the host lets do it.</summary>
/// <remarks>
/// <para>
/// This is the editor's authority, and it is deliberately narrower than a widened
/// <see cref="IPageWriter"/>: the host offers it to the content layer whose key is the editor page
/// and to nothing else, so a stored page cannot make itself a handle on the whole store. Reserved
/// keys are refused, a layout never sees it, and a <c>HEAD</c> gets the read members only, so a
/// validator can render the form without writing.
/// </para>
/// <para>
/// <strong>Every write requires a signed-in visitor.</strong> An anonymous render is refused with a
/// <see cref="UserFacingException"/> before anything is compiled or stored. A <c>HEAD</c>, an ended
/// or timed-out render, a second action in one render, an unaddressable key and a reserved key are
/// page bugs and throw <see cref="InvalidOperationException"/>; a key that names nothing is the
/// page's own answer through <see cref="Resolve"/>, not an exception.
/// </para>
/// <para>
/// A save is conditional on the head hash the form was rendered from. A mismatch answers
/// <see cref="PageEditOutcome.Stale"/> and writes nothing; the page re-reads the source and shows
/// the author what changed. A restore and a seed install are saves: nothing is rewound, a tombstone
/// comes back with its history, and one that would change nothing answers
/// <see cref="PageEditOutcome.Unchanged"/>.
/// </para>
/// </remarks>
public interface IPageEditor {

    /// <summary>Normalises a key and reports whether the site's own routes answer for it.</summary>
    /// <param name="key">The text the page was given.</param>
    /// <returns>The canonical key, or why the text names no page.</returns>
    PageKeyResolution Resolve(string key);

    /// <summary>The pages the release ships with, as keys.</summary>
    IReadOnlyList<string> SeedPaths();

    /// <summary>Reads the source this release ships for a seed.</summary>
    /// <param name="key">The page's key; normalised before it is read.</param>
    /// <returns>The bundled source, or <see langword="null"/> when the release ships no seed for that key.</returns>
    /// <remarks>
    /// A read, so it needs no signed-in visitor and is not the render's one action: a confirmation
    /// shows the diff between this and the live source, which is the one thing a page cannot read for
    /// itself. Reading it never grants a write — <see cref="InstallSeedAsync"/> decides that.
    /// </remarks>
    /// <exception cref="ArgumentException">The key cannot name a page.</exception>
    Task<string?> SeedSourceAsync(string key);

    /// <summary>Saves a page, only while its head still has the expected revision.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="template">The template text.</param>
    /// <param name="expectedHash">The head hash the form was rendered from.</param>
    /// <exception cref="UserFacingException">This render has no signed-in visitor, so nothing was saved.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a <c>HEAD</c>, an ended render or a second action.</exception>
    Task<PageEditResult> SaveAsync(string key, string template, string expectedHash);

    /// <summary>Tombstones a page, keeping its history.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <exception cref="UserFacingException">This render has no signed-in visitor.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, the editor itself, a <c>HEAD</c>, an ended render or a second action.</exception>
    Task<PageDeleteResult> DeleteAsync(string key);

    /// <summary>Saves a retained revision's source as a new revision.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="hash">The retained revision's hash, as the catalog reported it.</param>
    /// <param name="expectedHash">The live head the form was rendered from, or null for a tombstone.</param>
    /// <returns>Null when that page has no revision with that hash.</returns>
    /// <exception cref="UserFacingException">This render has no signed-in visitor.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a <c>HEAD</c>, an ended render or a second action.</exception>
    Task<PageEditResult?> RestoreRevisionAsync(string key, string hash, string? expectedHash);

    /// <summary>Saves a page the release ships with from its bundled source.</summary>
    /// <param name="key">The page's key; normalised and checked.</param>
    /// <param name="expectedHash">The live head the form was rendered from, or null for a tombstone.</param>
    /// <returns>Null when the key is not a seed.</returns>
    /// <exception cref="UserFacingException">This render has no signed-in visitor.</exception>
    /// <exception cref="InvalidOperationException">A reserved key, a <c>HEAD</c>, an ended render or a second action.</exception>
    Task<PageEditResult?> InstallSeedAsync(string key, string? expectedHash);
}
