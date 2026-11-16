// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a page writes into: its body, the settings applied to it, and the response shapes.</summary>
/// <remarks>
/// <para>
/// Reached as <c>ctx.Response</c>. It is contract rather than a capability, because every page always
/// has one; a helper can take this interface on its own to get the output and nothing else.
/// </para>
/// <para>
/// Status, content type and headers are applied after <see cref="IHandler.HandleAsync"/> completes. The
/// body is buffered until then, so a page that throws produces a clean error response rather than
/// half a document. A page that calls <see cref="FlushAsync"/> opts into sending its body while it
/// runs; from the first flush the response is committed and those settings are final.
/// </para>
/// </remarks>
public interface IPageResponse {

    /// <summary>Writes unescaped HTML to the output.</summary>
    /// <param name="message">The HTML to write.</param>
    public void WriteRaw(string message);

    /// <summary>Writes escaped HTML to the output.</summary>
    /// <param name="message">The text to escape and write.</param>
    public void Write(string message);

    /// <summary>Sends what has been written so far and commits the response.</summary>
    /// <returns>A task that completes when the bytes have been handed to the response.</returns>
    /// <remarks>
    /// <para>
    /// The first flush is the commit point: the status, content type, headers and script policy are
    /// applied and the response starts, so they are final from then on. A later
    /// <see cref="SetStatus"/>, <see cref="SetContentType"/>, <see cref="SetHeader"/>,
    /// <see cref="AllowScripts"/>, <see cref="Redirect"/> or read of <see cref="AntiforgeryField"/>
    /// throws instead of silently doing nothing. Writes after a flush are buffered until the next
    /// flush, and whatever remains when the handler returns is sent then.
    /// </para>
    /// <para>
    /// A page that flushes is served alone: a layout needs the whole body before it can run, so a
    /// flush while the page would be wrapped throws. Clear <c>IPageLayout.Layout</c> and write the
    /// page's own document first. A layout itself cannot flush.
    /// </para>
    /// <para>
    /// Streaming does not change the escaping rule: <see cref="Write"/> still encodes and
    /// <see cref="WriteRaw"/> still writes the caller's markup. A page that fails after its first
    /// flush has no error page to fall back on; the response is truncated and the failure is logged.
    /// The render budget still applies, so a stream is ended at its deadline, and a <c>HEAD</c>
    /// request commits the response but sends no body.
    /// </para>
    /// <para>
    /// The first flush is also where the render budget changes shape. Before it the render's whole
    /// budget applies; from it the response has its own silence and ceiling (<c>Pages:Stream</c>), the
    /// render slot is released and a stream slot is taken. A stream that goes quiet, or reaches its
    /// ceiling, is truncated; it is no longer cut by the render's timeout.
    /// </para>
    /// <para>
    /// This takes no cancellation token, like every other I/O member of the page contract: the render
    /// owns <see cref="IContext.Aborted"/> and the deadline and links them per call.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The render cannot stream, the page is a layout, it has redirected, or it is still wrapped in a
    /// layout.
    /// </exception>
    /// <exception cref="PageStreamRefusedException">
    /// Every stream slot is taken, so nothing was committed. The shell answers <c>503</c> unless the
    /// page catches this and writes its own document.
    /// </exception>
    public Task FlushAsync();

    /// <summary>Sets the response status code.</summary>
    /// <param name="status">The code to send, between 100 and 599.</param>
    public void SetStatus(int status);

    /// <summary>Sets the response content type.</summary>
    /// <param name="contentType">The media type to send.</param>
    public void SetContentType(string contentType);

    /// <summary>Sets a response header the page is allowed to control.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value; it may not contain a line break.</param>
    /// <exception cref="ArgumentException">
    /// The header is reserved to the shell, or the value cannot be sent safely.
    /// </exception>
    public void SetHeader(string name, string value);

    /// <summary>Allows this page to load same-origin scripts.</summary>
    /// <remarks>
    /// Widens the response's <c>Content-Security-Policy</c> from <c>script-src 'none'</c> to
    /// <c>script-src 'self'</c>. Inline scripts and event-handler attributes stay blocked, as do
    /// scripts from another origin: the page's script is another page, or a seeded asset, served
    /// with a JavaScript content type and referenced by <c>&lt;script src="/…"&gt;</c>. Calling this
    /// more than once is harmless.
    /// </remarks>
    public void AllowScripts();

    /// <summary>Redirects instead of writing a body.</summary>
    /// <param name="location">
    /// The target, relative or absolute. A relative target keeps its query and fragment, and its path
    /// is escaped for the header, so <c>/search?q=metrics</c> arrives as written.
    /// </param>
    /// <param name="status">The redirect status; 302 by default.</param>
    public void Redirect(string location, int status = 302);

    /// <summary>Sends an anonymous visitor to the sign-in form and back to this page afterwards.</summary>
    /// <remarks>
    /// <para>
    /// The target is the shell's own: <c>/login?returnUrl=</c> followed by this page's canonical path
    /// and raw query, so the visitor returns to exactly what they asked for. A fragment is not
    /// preserved, because the browser never sends one.
    /// </para>
    /// <para>
    /// The status is <c>302 Found</c> for <c>GET</c> and <c>HEAD</c>, and <c>303 See Other</c> for
    /// <c>POST</c>, so a submitted form is not replayed when the visitor comes back. This returns
    /// normally; a guard must also <c>return;</c> from the page.
    /// </para>
    /// </remarks>
    public void RedirectToLogin();

    /// <summary>The hidden input that carries this request's antiforgery pair.</summary>
    /// <remarks>
    /// <para>
    /// Write it raw, as <c>&lt;%== ctx.Response.AntiforgeryField %&gt;</c>, inside every form on a page
    /// an authenticated visitor can submit: the shell refuses an authenticated <c>POST</c> without a
    /// valid pair. It is generated once per render, so asking twice does not mint two tokens.
    /// </para>
    /// <para>
    /// Empty when the render was abandoned, which fails closed at the next <c>POST</c> rather than
    /// handing out a token nothing can verify.
    /// </para>
    /// </remarks>
    public string AntiforgeryField { get; }
}
