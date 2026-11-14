// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using System.Net;
using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;

/// <summary>The only <see cref="IContext"/> a page ever runs against.</summary>
/// <remarks>
/// <para>
/// Collects what the page asks for instead of applying it, so the response is written once the
/// handler has returned and a page that throws leaves nothing half-sent.
/// </para>
/// <para>
/// This is the only type a page's logging goes through, and the category and its scope are
/// operator-facing: changing them breaks configured filters and log queries.
/// </para>
/// </remarks>
#pragma warning disable CA1001 // One render's context; the flush gate never allocates a wait handle.
internal sealed class SimpleContext : IContext {

    // A page must not be able to clobber the shell's own cookies or framing. Everything that changes
    // the meaning of the body is reachable through a dedicated member instead. Caching policy is not
    // a page's to set: a page with code is per-visitor and the host sends no-store.
    private static readonly HashSet<string> s_allowedHeaders = [with(StringComparer.OrdinalIgnoreCase),
        "Content-Language"];

    private const string CustomHeaderPrefix = "X-";

    // One category for every page, with the page's key on a scope. It cannot be a category per path:
    // ILoggerFactory caches a logger for each distinct category for the life of the process and
    // never evicts one, so the key would have to come from the request — and a not-found request
    // picks its own URL.
    internal const string PageCategory = "uk.osric.asp.Pages";

    private readonly Func<string>? _antiforgeryFactory;
    private readonly Func<string, string>? _assetUrl;
    private readonly IPageBodySink? _bodySink;
    private readonly Lock _body = new();
    private readonly PageCapabilities _capabilities;
    private readonly PageCommitState _commit;
    private readonly Dictionary<string, string> _headers = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly string _editorPath;
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly string _loginRedirect;
    private readonly BodyBudget _bodyBudget;
    private readonly bool _isLayout;
    private readonly ILogger _logger;
    private readonly PagePath _path;
    private readonly PageRequest _request;
    private readonly StringWriter _writer;
    private readonly Func<string, bool>? _wouldWrap;

    // Written once, under the body lock, so two reads in one render hand out one token: the token and
    // its cookie are a pair, and minting a second would leave the first unverifiable.
    private string? _antiforgeryField;

    // Written by the abandoning thread and read by the page's task, so the flag is volatile rather than
    // merely lock-protected.
    private volatile bool _abandoned;
    private bool _allowsScripts;
    private string _contentType;
    private CancellationToken _deadline = CancellationToken.None;
    private string? _location;
    private int _status = PageResponse.Default.Status;

    /// <summary>Creates the context a page runs against.</summary>
    /// <param name="loggerFactory">Creates the one logger every page writes through.</param>
    /// <param name="request">The request snapshot.</param>
    /// <param name="aborted">The token from the request being served.</param>
    /// <param name="writer">The buffer the body is written to.</param>
    /// <param name="capabilities">The capabilities this layer offers.</param>
    /// <param name="contentTypes">The media type the page's key names.</param>
    /// <param name="maxBodyChars">How much body this layer may write when no chain budget is given.</param>
    /// <param name="user">The signed-in account, or <see langword="null"/> for an anonymous visitor.</param>
    /// <param name="loginRedirect">Where <see cref="RedirectToLogin"/> sends an anonymous visitor.</param>
    /// <param name="antiforgeryField">
    /// Builds the hidden antiforgery input, or <see langword="null"/> when the page must get nothing.
    /// </param>
    /// <param name="path">The layer's own key, which is the request's path for the content layer.</param>
    /// <param name="isLayout">
    /// Whether this layer is a layout, in which case the response belongs to the content: status,
    /// content type, headers and a redirect are ignored, because a layout is markup and nothing else.
    /// </param>
    /// <param name="bodyBudget">
    /// The allowance the whole chain shares; a layer on its own gets one of its own.
    /// </param>
    /// <param name="assetUrl">
    /// Resolves a link against the request's asset map, or <see langword="null"/> when the render has
    /// no map and every link is left as written.
    /// </param>
    /// <param name="editorPath">The editor key a layout's remedy links to.</param>
    /// <param name="commitState">
    /// Whether this layer has committed its response, shared with the capabilities that would need to
    /// set a header after the commit.
    /// </param>
    /// <param name="bodySink">Where a flushing page writes, or <see langword="null"/> when it cannot stream.</param>
    /// <param name="wouldWrap">
    /// Whether a response of this content type would be wrapped in a layout, in which case it cannot
    /// stream.
    /// </param>
#pragma warning disable CA1068 // CancellationToken parameters must come last
    public SimpleContext(
#pragma warning restore CA1068 // CancellationToken parameters must come last
        ILoggerFactory loggerFactory,
        PageRequest request,
        CancellationToken aborted,
        StringWriter writer,
        PageCapabilities capabilities,
        PageContentTypes contentTypes,
        int maxBodyChars,
        string editorPath,
        PageUser? user = null,
        string loginRedirect = "/login",
        Func<string>? antiforgeryField = null,
        PagePath? path = null,
        bool isLayout = false,
        BodyBudget? bodyBudget = null,
        Func<string, string>? assetUrl = null,
        PageCommitState? commitState = null,
        IPageBodySink? bodySink = null,
        Func<string, bool>? wouldWrap = null) {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(contentTypes);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginRedirect);
        ArgumentException.ThrowIfNullOrWhiteSpace(editorPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBodyChars, 1);

        // The path is canonical here: PageEndpoint redirects anything else before building a context.
        _logger = loggerFactory.CreateLogger(PageCategory);
        _bodyBudget = bodyBudget ?? new BodyBudget(maxBodyChars);
        _bodySink = bodySink;
        _commit = commitState ?? new PageCommitState();
        _isLayout = isLayout;
        _path = path ?? PagePath.Normalize(request.Path);
        _request = request;
        _loginRedirect = loginRedirect;
        _editorPath = editorPath;
        _antiforgeryFactory = antiforgeryField;
        _assetUrl = assetUrl;
        _wouldWrap = wouldWrap;
        Aborted = aborted;
        _capabilities = capabilities;
        _writer = writer;
        User = user;

        // The extension decides the initial value; SetContentType overrides it.
        _contentType = contentTypes.ForPath(_path.Key);
    }

    /// <inheritdoc/>
    public PageRequest Request => _request;

    /// <inheritdoc/>
    public string Path => _path.Key;

    /// <inheritdoc/>
    public string EditorPath => _editorPath;

    /// <inheritdoc/>
    public PageUser? User { get; }

    /// <inheritdoc/>
    public CancellationToken Aborted { get; }

    /// <inheritdoc/>
    public IPageCapabilities Capabilities => _capabilities;

    /// <summary>What the page asked the response to be.</summary>
    /// <remarks>
    /// Every page that runs code is <c>no-store</c>: it can keep per-visitor state, so its body has
    /// no business in a shared cache and no business inviting a conditional request. The host writes
    /// the same header after the page's own, so this is the default rather than the last word.
    /// </remarks>
    public PageResponse ToResponse() {
        _headers.TryAdd("Cache-Control", "no-store");
        return new(_status, _contentType, _headers, _location) { AllowsScripts = _allowsScripts };
    }

    /// <summary>The body written so far.</summary>
    public string GetBody() {
        lock (_body) {
            return _writer.ToString() ?? string.Empty;
        }
    }

    /// <summary>Whether the first flush has committed the response.</summary>
    public bool IsCommitted => _commit.Committed;

    /// <summary>Gives the page the render's deadline, so its I/O is cancelled with the render.</summary>
    /// <param name="deadline">The deadline's token; it outlives this context's use of it.</param>
    public void AttachDeadline(CancellationToken deadline) {
        _deadline = deadline;
        _capabilities.AttachDeadline(deadline);
    }

    /// <summary>Stops the page writing, once its render has been given up on.</summary>
    /// <remarks>
    /// The page cannot be stopped, so this is what keeps it from filling the buffer — or
    /// racing the response — after the site has answered without it.
    /// </remarks>
    public void Abandon() {
        lock (_body) {
            _abandoned = true;
        }

        _capabilities.Abandon();
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
    /// <exception cref="ArgumentNullException">The page passed null.</exception>
    public void Write(string message) {
        ArgumentNullException.ThrowIfNull(message);

        WriteBody(WebUtility.HtmlEncode(message));
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">The page passed null.</exception>
    public void WriteRaw(string message) {
        ArgumentNullException.ThrowIfNull(message);

        WriteBody(message);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// This render cannot stream, the page is a layout, it has redirected, or it is still wrapped in a
    /// layout.
    /// </exception>
    public async Task FlushAsync() {
        ThrowIfCannotStream();

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(Aborted, _deadline);
        CancellationToken cancellationToken = linked.Token;

        await _flushing.WaitAsync(cancellationToken);
        try {
            string text;
            bool first;

            lock (_body) {
                if (_abandoned) {
                    return;
                }

                text = TakeBuffer();
                first = !_commit.Committed;
                if (first) {
                    _commit.Committed = true;
                }
            }

            if (first) {
                await _bodySink!.StartAsync(ToResponse(), cancellationToken);
            }

            if (text.Length > 0) {
                await _bodySink!.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
            }
        } finally {
            _ = _flushing.Release();
        }
    }

    /// <summary>Writes whatever the page left buffered after its last flush.</summary>
    /// <remarks>
    /// Only a committed response has a body to complete; a buffered one is shaped from the context by
    /// the endpoint. A disconnect or a deadline that fires during this last write is the response
    /// ending, not a page fault.
    /// </remarks>
    public async Task CompleteAsync() {
        if (_bodySink is null || !_commit.Committed) {
            return;
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(Aborted, _deadline);
        CancellationToken cancellationToken = linked.Token;

        await _flushing.WaitAsync(cancellationToken);
        try {
            string text;

            lock (_body) {
                if (_abandoned) {
                    return;
                }

                text = TakeBuffer();
            }

            if (text.Length > 0) {
                await _bodySink.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
            }
        } catch (OperationCanceledException) {
            // The response is already committed and the client or the deadline has ended it; there is
            // nothing left to write and nothing to report as a page fault.
        } finally {
            _ = _flushing.Release();
        }
    }

    // Validated before the commit: a redirect has no body, a layout cannot stream, and a page that
    // would be wrapped has no place to put the bytes before the layout has run.
    private void ThrowIfCannotStream() {
        if (_isLayout) {
            throw new InvalidOperationException($"{Path} is a layout; a layout is markup and cannot stream.");
        }

        if (_bodySink is null) {
            throw new InvalidOperationException($"{Path} cannot stream its response in this render.");
        }

        if (_location is not null) {
            throw new InvalidOperationException($"{Path} has redirected; a redirect has no body to stream.");
        }

        if (_wouldWrap?.Invoke(_contentType) == true) {
            throw new InvalidOperationException(
                $"{Path} is wrapped in a layout, which needs its whole body before it can run. Set IPageLayout.Layout to null and write the page's own document before the first flush.");
        }
    }

    private string TakeBuffer() {
        StringBuilder buffer = _writer.GetStringBuilder();
        string text = buffer.ToString();
        _ = buffer.Clear();
        return text;
    }

    private void ThrowIfCommitted(string member) {
        if (_commit.Committed) {
            throw new InvalidOperationException(
                $"{Path} has already flushed its response; {member} must be called before the first flush.");
        }
    }

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

            // The cap exists to bound what is held in memory before the response can be sent. Once the
            // page has committed, its body is on the wire and a total cap would defeat streaming.
            if (!_commit.Committed && !_bodyBudget.TryCharge(text.Length)) {
                throw new InvalidOperationException(
                    $"{Path} wrote more than {_bodyBudget.MaxChars} characters; the render was stopped.");
            }

            _writer.Write(text);
        }
    }

    /// <inheritdoc/>
    public string AssetUrl(string href) {
        ArgumentNullException.ThrowIfNull(href);

        return _assetUrl?.Invoke(href) ?? href;
    }

    /// <inheritdoc/>
    public void SetStatus(int status) {
        ThrowIfCommitted(nameof(SetStatus));

        if (IgnoreAsLayout(nameof(SetStatus), status.ToString(CultureInfo.InvariantCulture))) {
            return;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(status, 100, nameof(status));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599, nameof(status));

        _status = status;
    }

    /// <inheritdoc/>
    public void SetContentType(string contentType) {
        ThrowIfCommitted(nameof(SetContentType));

        if (IgnoreAsLayout(nameof(SetContentType), contentType)) {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        _contentType = contentType;
    }

    /// <inheritdoc/>
    public void SetHeader(string name, string value) {
        ThrowIfCommitted(nameof(SetHeader));

        if (IgnoreAsLayout(nameof(SetHeader), name)) {
            return;
        }

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
        ThrowIfCommitted(nameof(AllowScripts));

        // The one response setting a layout shares: its script is part of its chrome, so the union
        // with the content's is what the response is served with.
        _allowsScripts = true;
    }

    /// <inheritdoc/>
    public void Redirect(string location, int status = 302) {
        ThrowIfCommitted(nameof(Redirect));

        if (IgnoreAsLayout(nameof(Redirect), location)) {
            return;
        }

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
            ThrowIfCommitted(nameof(AntiforgeryField));

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

    // A layout is markup and nothing else. The content ran first and owns the response, so an
    // attempt from a layout is ignored rather than allowed to change what the visitor asked for -
    // and logged, because a silent no-op is a puzzle for whoever wrote the layout.
    private bool IgnoreAsLayout(string member, string detail) {
        if (!_isLayout) {
            return false;
        }

        LogWarning($"{Path} called {member}({detail}); a layout cannot set the response.");
        return true;
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
#pragma warning restore CA1001
}
