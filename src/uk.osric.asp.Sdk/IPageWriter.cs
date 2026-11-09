// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Saves the calling page's own source.</summary>
/// <remarks>
/// <para>
/// The one write a page may make: its own template, under its own key. The handle is bound to
/// <see cref="IContext.Path"/>, so isolation is structural rather than a check — a page cannot name
/// another page — and a reserved key cannot be reached, because a reserved key never renders.
/// Writing a named key is a different capability (IDEA-017).
/// </para>
/// <para>
/// <strong>The write requires a signed-in visitor.</strong> An anonymous render is refused with a
/// <see cref="UserFacingException"/> before anything is compiled or stored. Reading the store is as
/// public as the page that calls it; changing what the site serves is not, so it needs an identity.
/// A page that would rather send the visitor to sign in writes the ordinary <c>ctx.User</c> guard
/// over its own form, and that guard is a courtesy in front of this refusal, never a replacement.
/// </para>
/// <para>
/// A save belongs to the next request. The running image is already loaded, so the response in hand
/// is the page as it was unless the page shows the result the save returned; the save is committed
/// before the rest of the render, and one render makes at most one save.
/// </para>
/// <para>
/// The save is bounded by the render but not cancelled by it. A save that has not started when the
/// render ends — it was abandoned, or its deadline passed — is refused; one that has started runs to
/// the compile's own timeout and the revision is stored, because losing the author's edit is the
/// worse failure. A save still queued for a compile slot when the render gives up ends in
/// cancellation, which is the shell's answer for every bounded call.
/// </para>
/// <para>
/// Two refusals are page bugs rather than visitor states, and throw
/// <see cref="InvalidOperationException"/>: a save during a <c>HEAD</c> render (which runs the page
/// once per validator, so a write would count as a visit) and a second save in one render. Save on a
/// <c>POST</c>, answer with the result, and let the next request serve the page.
/// </para>
/// </remarks>
public interface IPageWriter {

    /// <summary>Saves this page's source as a new revision.</summary>
    /// <param name="template">The template text. It may be empty; an empty page is a page.</param>
    /// <returns>The new revision's hash and the compiler's author-facing diagnostics.</returns>
    /// <exception cref="ArgumentNullException">The template is <see langword="null"/>.</exception>
    /// <exception cref="UserFacingException">
    /// This render has no signed-in visitor, so nothing was saved.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The request is a <c>HEAD</c>, the render was abandoned or its deadline passed before the save
    /// started, or this render has already saved.
    /// </exception>
    public Task<PageSaveResult> SaveAsync(string template);
}
