// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using System.Net;
using uk.osric.asp.Core;

/// <summary>The only <see cref="IContext"/> a page ever runs against.</summary>
/// <remarks>
/// Collects what the page asks for instead of applying it, so the response is written once the
/// handler has returned and a page that throws leaves nothing half-sent.
/// </remarks>
public sealed class SimpleContext : IContext {

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

    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;
    private readonly PageRequest _request;
    private readonly TextWriter _writer;

    private string _contentType = PageResponse.Default.ContentType;
    private string? _location;
    private int _status = PageResponse.Default.Status;

    /// <summary>Creates the context a page runs against.</summary>
    /// <param name="logger">Where <see cref="Log"/> writes.</param>
    /// <param name="request">The request snapshot.</param>
    /// <param name="aborted">The token from the request being served.</param>
    /// <param name="writer">The buffer the body is written to.</param>
    public SimpleContext(ILogger logger, PageRequest request, CancellationToken aborted, TextWriter writer) {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);

        _logger = logger;
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

    /// <inheritdoc/>
    public void Log(string message) {
        _logger.LogInformation("Path: {Path} - {Message}", _request.Path, message);
    }

    /// <inheritdoc/>
    public void Write(string message) => _writer.Write(WebUtility.HtmlEncode(message));

    /// <inheritdoc/>
    public void WriteRaw(string message) => _writer.Write(message);

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
