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
/// This is the request side of the contract: what a page can read and ask for. What it writes into is
/// <see cref="Response"/>, an <see cref="IPageResponse"/>, and that is where the buffering and the
/// commit rule live.
/// </para>
/// <para>
/// The handler is asynchronous, so <c>await</c> is an ordinary expression in a code block and inside
/// a write expression. Every member that does I/O — here or on a capability reached through
/// <see cref="Capabilities"/> — returns a <see cref="Task"/> and takes no cancellation token: the render
/// owns <see cref="Aborted"/> and the deadline, and the implementation links them per call.
/// </para>
/// </remarks>
public interface IContext {

    /// <summary>The request the page is running against.</summary>
    public PageRequest Request { get; }

    /// <summary>The canonical key of the page being rendered.</summary>
    public string Path { get; }

    /// <summary>The canonical key of the site's editor page, so a page can link to it.</summary>
    /// <remarks>
    /// The editor's key is the host's policy — <c>Pages:Editor:Key</c> — so a page that hard-codes it
    /// breaks when the owner moves the editor. The editor's own page is served at this key and takes
    /// the page to edit as <c>?edit=</c>, which makes
    /// <c>ctx.EditorPath + "?edit=" + System.Uri.EscapeDataString(ctx.Path)</c> the way to offer
    /// "edit this page".
    /// </remarks>
    public string EditorPath { get; }

    /// <summary>The signed-in local account, or <see langword="null"/> for an anonymous visitor.</summary>
    /// <remarks>
    /// The identity is supplied and validated by the shell; a page cannot forge it with a header,
    /// cookie or form field. A page is public unless its own code checks this value, so the ordinary
    /// guard is <c>if (ctx.User is not { } user) { ctx.Response.RedirectToLogin(); return; }</c>.
    /// </remarks>
    public PageUser? User { get; }

    /// <summary>Aborted when the request is abandoned; pages may observe it to stop early.</summary>
    public CancellationToken Aborted { get; }

    /// <summary>What the page writes into: its body, the settings applied to it and the response shapes.</summary>
    /// <remarks>
    /// This is not a capability. Every page always has a response, so it is on the context; a page that
    /// needs none of the rest still needs this.
    /// </remarks>
    public IPageResponse Response { get; }

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

    /// <summary>Versions a same-origin link when the page it names is a live asset.</summary>
    /// <param name="href">The link as the page would write it.</param>
    /// <returns>
    /// The link with a <c>?v=</c> hash, or the link unchanged when it is not a bare site-relative
    /// path or does not name a live asset. A query or fragment, an absolute or protocol-relative URL
    /// and a missing, deleted or compiled page all keep the link as written, so an unversioned
    /// fallback still works.
    /// </returns>
    /// <remarks>
    /// An asset is a page with no code: a complete representation whose source is the response. The
    /// hash pins the retained revision, so editing the asset does not change a URL that already
    /// points at it.
    /// </remarks>
    public string AssetUrl(string href);

    /// <summary>The capabilities this render offers.</summary>
    /// <remarks>
    /// Storage, outbound HTTP, the document's slots and Markdown live here rather than on the
    /// context, because a page that needs none of them should never meet them. A capability the
    /// render does not offer is absent rather than <see langword="null"/>; a page decides whether
    /// that is fatal with <see cref="IPageCapabilities.Get{T}"/> or
    /// <see cref="IPageCapabilities.GetRequired{T}"/>.
    /// </remarks>
    public IPageCapabilities Capabilities { get; }

    /// <summary>Writes unescaped HTML to the output. Deprecated: use <see cref="Response"/>.</summary>
    /// <param name="message">The HTML to write.</param>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public void WriteRaw(string message) => Response.WriteRaw(message);

    /// <summary>Writes escaped HTML to the output. Deprecated: use <see cref="Response"/>.</summary>
    /// <param name="message">The text to escape and write.</param>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public void Write(string message) => Response.Write(message);

    /// <summary>Sends what has been written so far and commits the response. Deprecated: use <see cref="Response"/>.</summary>
    /// <returns>A task that completes when the bytes have been handed to the response.</returns>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public Task FlushAsync() => Response.FlushAsync();

    /// <summary>Sets the response status code. Deprecated: use <see cref="Response"/>.</summary>
    /// <param name="status">The code to send, between 100 and 599.</param>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public void SetStatus(int status) => Response.SetStatus(status);

    /// <summary>Sets the response content type. Deprecated: use <see cref="Response"/>.</summary>
    /// <param name="contentType">The media type to send.</param>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public void SetContentType(string contentType) => Response.SetContentType(contentType);

    /// <summary>Sets a response header the page is allowed to control. Deprecated: use <see cref="Response"/>.</summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value; it may not contain a line break.</param>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public void SetHeader(string name, string value) => Response.SetHeader(name, value);

    /// <summary>Allows this page to load same-origin scripts. Deprecated: use <see cref="Response"/>.</summary>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public void AllowScripts() => Response.AllowScripts();

    /// <summary>Redirects instead of writing a body. Deprecated: use <see cref="Response"/>.</summary>
    /// <param name="location">The target, relative or absolute.</param>
    /// <param name="status">The redirect status; 302 by default.</param>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public void Redirect(string location, int status = 302) => Response.Redirect(location, status);

    /// <summary>Sends an anonymous visitor to the sign-in form. Deprecated: use <see cref="Response"/>.</summary>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public void RedirectToLogin() => Response.RedirectToLogin();

    /// <summary>The hidden input that carries this request's antiforgery pair. Deprecated: use <see cref="Response"/>.</summary>
    [Obsolete(PageResponseDeprecation.Deprecated, DiagnosticId = "PAGE009")]
    public string AntiforgeryField => Response.AntiforgeryField;
}
