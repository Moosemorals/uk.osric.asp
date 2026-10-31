// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>What a page can read from the request and ask of the response.</summary>
/// <remarks>
/// <para>
/// This is the whole contract a stored page is written against, and the only type a page sees that
/// touches the request. Everything on it is a plain value, so the runtime never hands page code an
/// ASP.NET type.
/// </para>
/// <para>
/// Status, content type and headers are applied after <see cref="IHandler.Handle"/> returns. The body
/// is buffered until then, so a page that throws produces a clean error response rather than half a
/// document.
/// </para>
/// </remarks>
public interface IContext {

    /// <summary>The request the page is running against.</summary>
    public PageRequest Request { get; }

    /// <summary>The canonical key of the page being rendered.</summary>
    public string Path { get; }

    /// <summary>Aborted when the request is abandoned; pages may observe it to stop early.</summary>
    public CancellationToken Aborted { get; }

    /// <summary>Logs a message to the environment.</summary>
    /// <param name="message">The message to log.</param>
    public void Log(string message);

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

    /// <summary>Redirects instead of writing a body.</summary>
    /// <param name="location">The target, relative or absolute.</param>
    /// <param name="status">The redirect status; 302 by default.</param>
    public void Redirect(string location, int status = 302);
}
