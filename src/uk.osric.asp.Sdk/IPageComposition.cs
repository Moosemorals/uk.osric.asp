// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a page can render beside itself, and around itself.</summary>
/// <remarks>
/// <para>
/// Reached through <see cref="IContext.Capabilities"/> as
/// <c>ctx.Capabilities.GetRequired&lt;IPageComposition&gt;()</c>, so a page that never composes never
/// meets it. Every layer of a render is offered it, so a wrapper can embed a header and a footer
/// partial, and a page can ask to be wrapped without knowing whether it is embedded.
/// </para>
/// <para>
/// An embedded page is a render of the whole page: its body, every wrapper it declares, its own key,
/// storage and session, the caller's identity, one budget and one slot, and no response of its own.
/// It is composition, not isolation: it has the capabilities a content page has, may write the store
/// and may start a job.
/// </para>
/// <para>
/// A request is the input in both directions. <see langword="null"/> means the caller's own request;
/// one that is given runs as it stands, except that <see cref="PageRequest.Path"/> is always the page
/// the visitor asked for, which the host pins. <see cref="PageRequestExtensions"/> copies and changes
/// a request without adding members to the record.
/// </para>
/// </remarks>
public interface IPageComposition {

    /// <summary>Renders another stored page where the call is, wrappers and all.</summary>
    /// <param name="key">The embedded page's canonical key, such as <c>/cards/post</c>.</param>
    /// <param name="request">The request to run it against, or <see langword="null"/> for this page's.</param>
    /// <returns>The outermost layer's frame; the body has already been written.</returns>
    /// <exception cref="ArgumentException">The key is not a page address.</exception>
    /// <exception cref="InvalidOperationException">
    /// The page is already being rendered in this chain with the same request, or the render's depth
    /// or fan-out limit has been reached.
    /// </exception>
    /// <exception cref="UserFacingException">
    /// The key names no page, or one that cannot be served; the embedded page redirects; or its
    /// wrapper cannot be served.
    /// </exception>
    /// <exception cref="OperationCanceledException">The render's deadline fired while it ran.</exception>
    /// <remarks>
    /// The frame is what the embedded page contributes outside its body, so a caller that wants its
    /// title, head or sheets folds them into its own slots. The call writes the outermost body, so a
    /// failure that is caught leaves nothing half-written.
    /// </remarks>
    public Task<PageFrame> EmbedAsync(string key, PageRequest? request = null);

    /// <summary>Renders another stored page, and returns its body instead of writing it.</summary>
    /// <param name="key">The captured page's canonical key.</param>
    /// <param name="request">The request to run it against, or <see langword="null"/> for this page's.</param>
    /// <returns>The outermost layer's body and frame; nothing was written.</returns>
    /// <exception cref="ArgumentException">The key is not a page address.</exception>
    /// <exception cref="InvalidOperationException">
    /// The page is already being rendered in this chain with the same request, or the render's depth
    /// or fan-out limit has been reached.
    /// </exception>
    /// <exception cref="UserFacingException">
    /// The key names no page, or one that cannot be served; the captured page redirects; or its
    /// wrapper cannot be served.
    /// </exception>
    /// <exception cref="OperationCanceledException">The render's deadline fired while it ran.</exception>
    /// <remarks>
    /// The same render as <see cref="EmbedAsync"/> with the write left out, for a page that wants to
    /// transform another page's output, or keep it for later. Its script permission is not applied to
    /// this response: a caller that adopts the body calls <see cref="IPageResponse.AllowScripts"/>
    /// itself when <see cref="PageFrame.AllowsScripts"/> says the body needs it.
    /// </remarks>
    public Task<PageCapture> CaptureAsync(string key, PageRequest? request = null);

    /// <summary>Declares a page that wraps this one, outside any layer already declared.</summary>
    /// <param name="key">The wrapper's canonical key, such as <c>/_markdown</c>.</param>
    /// <param name="request">The request to run the wrapper against, or <see langword="null"/> for this page's.</param>
    /// <exception cref="ArgumentException">The key is not a page address.</exception>
    /// <exception cref="InvalidOperationException">
    /// The key is a reserved path, or the response has already been flushed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A declaration, not a handover: the host runs the chain after the page returns, so a guard's
    /// early <c>return;</c> is still wrapped and the page can set its status after writing its body.
    /// Calls stack outward, so the first is the innermost layer this page names and each later call
    /// wraps the result of the previous one. A wrapper may add its own outside it.
    /// </para>
    /// <para>
    /// The wrapper receives this layer's <see cref="PageCapture"/> as <see cref="IPageLayout.Content"/>
    /// and decides what to fold. A page that declares nothing is served exactly as it wrote itself.
    /// </para>
    /// </remarks>
    public void Wrap(string key, PageRequest? request = null);

    /// <summary>Declares the site's layout as the page that wraps this one.</summary>
    /// <exception cref="InvalidOperationException">The response has already been flushed.</exception>
    /// <remarks>
    /// The key is the host's policy, so a page does not hard-code one the owner can move. When the
    /// site is configured with no default layout, this records nothing.
    /// </remarks>
    public void Wrap();
}
