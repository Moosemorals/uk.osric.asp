// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a page can read from the request and ask of the response.</summary>
/// <remarks>
/// <para>
/// This is the whole contract a stored page is written against, and the only type a page sees that
/// touches the request. Everything on it is a plain value, so the runtime never hands page code an
/// ASP.NET type.
/// </para>
/// <para>
/// Status, content type and headers are applied after <see cref="IHandler.HandleAsync"/> completes. The
/// body is buffered until then, so a page that throws produces a clean error response rather than
/// half a document.
/// </para>
/// <para>
/// The handler is asynchronous, so <c>await</c> is an ordinary expression in a code block and inside
/// a write expression. Every member that does I/O — here or on a capability reached through
/// <see cref="Features"/> — returns a <see cref="Task"/> and takes no cancellation token: the render
/// owns <see cref="Aborted"/> and the deadline, and the implementation links them per call.
/// </para>
/// </remarks>
public interface IContext {

    /// <summary>The request the page is running against.</summary>
    public PageRequest Request { get; }

    /// <summary>The canonical key of the page being rendered.</summary>
    public string Path { get; }

    /// <summary>The signed-in local account, or <see langword="null"/> for an anonymous visitor.</summary>
    /// <remarks>
    /// The identity is supplied and validated by the shell; a page cannot forge it with a header,
    /// cookie or form field. A page is public unless its own code checks this value, so the ordinary
    /// guard is <c>if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; }</c>.
    /// </remarks>
    public PageUser? User { get; }

    /// <summary>Aborted when the request is abandoned; pages may observe it to stop early.</summary>
    public CancellationToken Aborted { get; }

    /// <summary>Logs a message at information level.</summary>
    /// <param name="message">The message to log.</param>
    /// <remarks>
    /// Entries are filed under a category shared by every page, with <see cref="Path"/> on a scope,
    /// so a page's lines can be filtered or levelled without touching another page's.
    /// </remarks>
    public void Log(string message);

    /// <summary>Logs a message at warning level.</summary>
    /// <param name="message">The message to log.</param>
    public void LogWarning(string message);

    /// <summary>Logs a message at error level.</summary>
    /// <param name="message">The message to log.</param>
    public void LogError(string message);

    /// <summary>Writes unescaped HTML to the output.</summary>
    /// <param name="message">The HTML to write.</param>
    public void WriteRaw(string message);

    /// <summary>Writes escaped HTML to the output.</summary>
    /// <param name="message">The text to escape and write.</param>
    public void Write(string message);

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
    /// Write it raw, as <c>&lt;%== ctx.AntiforgeryField %&gt;</c>, inside every form on a page an
    /// authenticated visitor can submit: the shell refuses an authenticated <c>POST</c> without a
    /// valid pair. It is generated once per render, so asking twice does not mint two tokens.
    /// </para>
    /// <para>
    /// Empty when the render was abandoned, which fails closed at the next <c>POST</c> rather than
    /// handing out a token nothing can verify.
    /// </para>
    /// </remarks>
    public string AntiforgeryField { get; }

    /// <summary>The capabilities this render offers.</summary>
    /// <remarks>
    /// Storage and outbound HTTP live here rather than on the context, because a page that needs
    /// neither should never meet them. A capability the render does not offer is absent rather than
    /// <see langword="null"/>; a page decides whether that is fatal with
    /// <see cref="IPageFeatures.Get{T}"/> or <see cref="IPageFeatures.GetRequired{T}"/>.
    /// </remarks>
    public IPageFeatures Features { get; }
}
