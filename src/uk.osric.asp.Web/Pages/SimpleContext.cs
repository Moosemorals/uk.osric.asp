// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;

/// <summary>The only <see cref="IContext"/> a page ever runs against.</summary>
/// <remarks>
/// <para>
/// Collects what the page asks for instead of applying it, so the response is written once the
/// handler has returned and a page that throws leaves nothing half-sent.
/// </para>
/// <para>
/// This is the only type a page's logging goes through. Every entry is filed under the one
/// <c>uk.osric.asp.Pages</c> category with the page's key on a <c>Path</c> scope property, so a page
/// can be filtered or levelled on its own. That category is operator-facing: changing it breaks
/// configured filters and log queries.
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

    // One category for every page, with the page's key on a scope. It cannot be a category per path:
    // ILoggerFactory caches a logger for each distinct category for the life of the process and
    // never evicts one, so the key would have to come from the request — and a not-found request
    // picks its own URL.
    internal const string PageCategory = "uk.osric.asp.Pages";

    private readonly Func<string>? _antiforgeryFactory;
    private readonly Lock _body = new();
    private readonly PageFeatures _features;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _loginRedirect;
    private readonly ILogger _logger;
    private readonly int _maxBodyChars;
    private readonly PageRequest _request;
    private readonly TextWriter _writer;

    // Written once, under the body lock, so two reads in one render hand out one token: the token and
    // its cookie are a pair, and minting a second would leave the first unverifiable.
    private string? _antiforgeryField;

    // Read from the page's task and written by the abandoning thread, which no longer share one, so
    // the flag is volatile rather than merely lock-protected.
    private volatile bool _abandoned;
    private bool _allowsScripts;
    private string _contentType = PageResponse.Default.ContentType;
    private string? _location;
    private int _status = PageResponse.Default.Status;
    private int _written;

    /// <summary>Creates the context a page runs against.</summary>
    /// <param name="loggerFactory">Creates the one logger every page writes through.</param>
    /// <param name="request">The request snapshot.</param>
    /// <param name="aborted">The token from the request being served.</param>
    /// <param name="writer">The buffer the body is written to.</param>
    /// <param name="features">The capabilities this layer offers.</param>
    /// <param name="maxBodyChars">How much body the page may write before it is refused.</param>
    /// <param name="user">The signed-in account, or <see langword="null"/> for an anonymous visitor.</param>
    /// <param name="loginRedirect">Where <see cref="RedirectToLogin"/> sends an anonymous visitor.</param>
    /// <param name="antiforgeryField">
    /// Builds the hidden antiforgery input, or <see langword="null"/> when the page must get nothing.
    /// </param>
    public SimpleContext(
        ILoggerFactory loggerFactory,
        PageRequest request,
        CancellationToken aborted,
        TextWriter writer,
        PageFeatures features,
        int maxBodyChars = RenderOptions.DefaultMaxBodyChars,
        PageUser? user = null,
        string loginRedirect = "/login",
        Func<string>? antiforgeryField = null) {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginRedirect);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBodyChars, 1);

        // The path is canonical here: PageEndpoint redirects anything else before building a context.
        _logger = loggerFactory.CreateLogger(PageCategory);
        _maxBodyChars = maxBodyChars;
        _request = request;
        _loginRedirect = loginRedirect;
        _antiforgeryFactory = antiforgeryField;
        Aborted = aborted;
        _features = features;
        _writer = writer;
        User = user;
    }

    /// <inheritdoc/>
    public PageRequest Request => _request;

    /// <inheritdoc/>
    public string Path => _request.Path;

    /// <inheritdoc/>
    public PageUser? User { get; }

    /// <inheritdoc/>
    public CancellationToken Aborted { get; }

    /// <inheritdoc/>
    public IPageFeatures Features => _features;

    /// <summary>What the page asked the response to be.</summary>
    public PageResponse ToResponse() =>
        new(_status, _contentType, _headers, _location) { AllowsScripts = _allowsScripts };

    /// <summary>The body written so far.</summary>
    public string GetBody() {
        lock (_body) {
            return _writer.ToString() ?? string.Empty;
        }
    }

    /// <summary>Gives the page the render's deadline, so its I/O is cancelled with the render.</summary>
    /// <param name="deadline">The deadline's token; it outlives this context's use of it.</param>
    public void AttachDeadline(CancellationToken deadline) => _features.AttachDeadline(deadline);

    /// <summary>Stops the page writing, once its render has been given up on.</summary>
    /// <remarks>
    /// The page cannot be stopped, so this is what keeps it from filling the buffer — or
    /// racing the response — after the site has answered without it.
    /// </remarks>
    public void Abandon() {
        lock (_body) {
            _abandoned = true;
        }

        _features.Abandon();
    }

    /// <inheritdoc/>
    public void Log(string message) {
        ArgumentNullException.ThrowIfNull(message);

        LogAt(LogLevel.Information, message);
    }

    /// <inheritdoc/>
    public void LogWarning(string message) {
        ArgumentNullException.ThrowIfNull(message);

        LogAt(LogLevel.Warning, message);
    }

    /// <inheritdoc/>
    public void LogError(string message) {
        ArgumentNullException.ThrowIfNull(message);

        LogAt(LogLevel.Error, message);
    }

    /// <inheritdoc/>
    public void Write(string message) => WriteBody(WebUtility.HtmlEncode(message));

    /// <inheritdoc/>
    public void WriteRaw(string message) => WriteBody(message);

    // The page's identity belongs on the scope, not the category: see PageCategory.
    private void LogAt(LogLevel level, string message) {
        // Checked first so a filtered level costs what it did through LogInformation and friends,
        // rather than a scope the provider will never see.
        if (!_logger.IsEnabled(level)) {
            return;
        }

        using IDisposable? scope = _logger.BeginScope("Page {Path}", Path);
        _logger.Log(level, "{Message}", message);
    }

    // The buffer is shared with a page task that may still be running after the render was
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
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!IsAllowed(name)) {
            throw new ArgumentException($"{name} is set by the site, not by a page.", nameof(name));
        }

        if (value.Contains('\r') || value.Contains('\n')) {
            throw new ArgumentException("A header value may not contain a line break.", nameof(value));
        }

        _headers[name] = value;
    }

    /// <inheritdoc/>
    public void AllowScripts() {
        _allowsScripts = true;
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

    /// <inheritdoc/>
    public void RedirectToLogin() {
        // A form post that turns into a sign-in must not be replayed as a post when the visitor comes
        // back. Pages only answer GET, POST and HEAD, so anything else takes the GET shape.
        int status = string.Equals(_request.Method, "POST", StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status303SeeOther
            : StatusCodes.Status302Found;

        Redirect(_loginRedirect, status);
    }

    /// <inheritdoc/>
    public string AntiforgeryField {
        get {
            if (_antiforgeryFactory is null) {
                return string.Empty;
            }

            // Generated under the body lock so an abandoned render cannot mint a token after the
            // response has gone, which would leave a cookie nothing can verify.
            lock (_body) {
                if (_abandoned) {
                    return string.Empty;
                }

                return _antiforgeryField ??= _antiforgeryFactory();
            }
        }
    }

    private static bool IsAllowed(string name) {
        // The X- wildcard would otherwise reach the shell's own X-Content-Type-Options and friends,
        // and a page clearing one silently weakens the response it is served in.
        if (SecurityHeaders.IsOwned(name)) {
            return false;
        }

        return s_allowedHeaders.Contains(name)
            || name.StartsWith(CustomHeaderPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
