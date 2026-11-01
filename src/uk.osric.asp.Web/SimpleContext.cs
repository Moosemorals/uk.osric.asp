// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using System.Net;
using uk.osric.asp.Core;
using uk.osric.asp.Web.Pages;

/// <summary>The only <see cref="IContext"/> a page ever runs against.</summary>
/// <remarks>
/// <para>
/// Collects what the page asks for instead of applying it, so the response is written once the
/// handler has returned and a page that throws leaves nothing half-sent.
/// </para>
/// <para>
/// This is the only type a page's logging goes through, and every entry it writes is filed
/// under <c>uk.osric.asp.Pages.{path}</c>, so a page can be filtered or levelled on its own. The
/// category format is operator-facing: changing it breaks configured filters and log queries.
/// </para>
/// </remarks>
internal sealed class SimpleContext : IContext {

    // A page must not be able to clobber the shell's own cookies or framing. Everything that changes
    // the meaning of the body is reachable through a dedicated member instead.
    private static readonly HashSet<string> s_allowedHeaders = new(StringComparer.OrdinalIgnoreCase) {
        "Cache-Control",
        "Content-Language",
        "ETag",
        "Expires",
        "Vary",
    };

    private const string CustomHeaderPrefix = "X-";

    private const string PageCategoryPrefix = "uk.osric.asp.Pages.";

    private readonly Lock _body = new();
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;
    private readonly int _maxBodyChars;
    private readonly PageRequest _request;
    private readonly TextWriter _writer;

    private bool _abandoned;
    private string _contentType = PageResponse.Default.ContentType;
    private string? _location;
    private int _status = PageResponse.Default.Status;
    private int _written;

    /// <summary>Creates the context a page runs against.</summary>
    /// <param name="loggerFactory">Creates the logger named for the page.</param>
    /// <param name="request">The request snapshot.</param>
    /// <param name="aborted">The token from the request being served.</param>
    /// <param name="writer">The buffer the body is written to.</param>
    /// <param name="maxBodyChars">How much body the page may write before it is refused.</param>
    public SimpleContext(
        ILoggerFactory loggerFactory,
        PageRequest request,
        CancellationToken aborted,
        TextWriter writer,
        int maxBodyChars = RenderOptions.DefaultMaxBodyChars) {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBodyChars, 1);

        // The path is canonical here: MainEndpoint redirects anything else before building a context.
        _logger = loggerFactory.CreateLogger(PageCategoryPrefix + request.Path);
        _maxBodyChars = maxBodyChars;
        _request = request;
        Aborted = aborted;
        _writer = writer;
    }

    /// <inheritdoc/>
    public PageRequest Request => _request;

    /// <inheritdoc/>
    public string Path => _request.Path;

    /// <inheritdoc/>
    public CancellationToken Aborted { get; }

    /// <summary>What the page asked the response to be.</summary>
    public PageResponse ToResponse() => new(_status, _contentType, _headers, _location);

    /// <summary>The body written so far.</summary>
    public string GetBody() {
        lock (_body) {
            return _writer.ToString() ?? string.Empty;
        }
    }

    /// <summary>Stops the page writing, once its render has been given up on.</summary>
    /// <remarks>
    /// The page's thread cannot be stopped, so this is what keeps it from filling the buffer — or
    /// racing the response — after the site has answered without it.
    /// </remarks>
    public void Abandon() {
        lock (_body) {
            _abandoned = true;
        }
    }

    /// <inheritdoc/>
    public void Log(string message) {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogInformation("{Message}", message);
    }

    /// <inheritdoc/>
    public void LogWarning(string message) {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogWarning("{Message}", message);
    }

    /// <inheritdoc/>
    public void LogError(string message) {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogError("{Message}", message);
    }

    /// <inheritdoc/>
    public void Write(string message) => WriteBody(WebUtility.HtmlEncode(message));

    /// <inheritdoc/>
    public void WriteRaw(string message) => WriteBody(message);

    // The buffer is shared with a page thread that may still be running after the render was
    // abandoned, so every write and the read that shapes the response happen under one lock.
    private void WriteBody(string text) {
        lock (_body) {
            if (_abandoned) {
                return;
            }

            if (_written + text.Length > _maxBodyChars) {
                throw new InvalidOperationException(
                    $"{Path} wrote more than {_maxBodyChars} characters; the render was stopped.");
            }

            _written += text.Length;
            _writer.Write(text);
        }
    }

    /// <inheritdoc/>
    public void SetStatus(int status) {
        if (status is < 100 or > 599) {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A status code must be between 100 and 599.");
        }

        _status = status;
    }

    /// <inheritdoc/>
    public void SetContentType(string contentType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        _contentType = contentType;
    }

    /// <inheritdoc/>
    public void SetHeader(string name, string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!IsAllowed(name)) {
            throw new ArgumentException($"{name} is set by the site, not by a page.", nameof(name));
        }

        if (value.Contains('\r') || value.Contains('\n')) {
            throw new ArgumentException("A header value may not contain a line break.", nameof(value));
        }

        _headers[name] = value;
    }

    /// <inheritdoc/>
    public void Redirect(string location, int status = 302) {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        if (location.Contains('\r') || location.Contains('\n')) {
            throw new ArgumentException("A redirect target may not contain a line break.", nameof(location));
        }

        SetStatus(status);
        _location = location;
    }

    private static bool IsAllowed(string name) {
        return s_allowedHeaders.Contains(name)
            || name.StartsWith(CustomHeaderPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
