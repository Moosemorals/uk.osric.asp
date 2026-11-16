// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using System.Net;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>The only <see cref="IContext"/> a page ever runs against.</summary>
/// <remarks>
/// <para>
/// The page's contract and its rules about who may ask: the commit gate, the layout rule and the
/// antiforgery token are this type's own, and what the page asks for is collected by
/// <see cref="PageBody"/> and <see cref="PageResponseState"/>. The response is written once the
/// handler has returned, so a page that throws leaves nothing half-sent.
/// </para>
/// <para>
/// This is the only type a page's logging goes through, and the category and its scope are
/// operator-facing: changing them breaks configured filters and log queries.
/// </para>
/// </remarks>
internal sealed class SimpleContext : IContext {

    // One category for every page, with the page's key on a scope. It cannot be a category per path:
    // ILoggerFactory caches a logger for each distinct category for the life of the process and
    // never evicts one, so the key would have to come from the request — and a not-found request
    // picks its own URL.
    internal const string PageCategory = "uk.osric.asp.Pages";

    private readonly Func<string>? _antiforgeryFactory;
    private readonly Func<string, string>? _assetUrl;
    private readonly PageBody _body;
    private readonly PageCapabilities _capabilities;
    private readonly string _editorPath;
    private readonly bool _isLayout;
    private readonly ILogger _logger;
    private readonly string _loginRedirect;
    private readonly PagePath _path;
    private readonly PageRequest _request;
    private readonly PageResponseState _response;
    private readonly Func<string, bool>? _wouldWrap;

    // Written once, under the body gate, so two reads in one render hand out one token: the token and
    // its cookie are a pair, and minting a second would leave the first unverifiable.
    private string? _antiforgeryField;

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
        User = user;

        // The budget and the commit are shared by the whole chain; a layer on its own gets one of each.
        _body = new PageBody(
            writer,
            _path,
            bodyBudget ?? new BodyBudget(maxBodyChars),
            commitState ?? new PageCommitState(),
            bodySink,
            aborted);

        // The extension decides the initial value; SetContentType overrides it.
        _response = new PageResponseState(contentTypes.ForPath(_path.Key));
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
    public PageResponse ToResponse() => _response.ToResponse();

    /// <summary>The body written so far.</summary>
    public string GetBody() => _body.GetText();

    /// <summary>Whether the first flush has committed the response.</summary>
    public bool IsCommitted => _body.IsCommitted;

    /// <summary>Gives the page the render's lifetime, so its I/O and its stream share one clock.</summary>
    /// <param name="lifetime">The lifetime the renderer opened for this request.</param>
    public void AttachLifetime(RenderLifetime lifetime) {
        ArgumentNullException.ThrowIfNull(lifetime);

        _body.AttachLifetime(lifetime);
        _capabilities.AttachLifetime(lifetime);
    }

    /// <summary>Stops the page writing, once its render has been given up on.</summary>
    /// <remarks>
    /// The page cannot be stopped, so this is what keeps it from filling the buffer — or
    /// racing the response — after the site has answered without it.
    /// </remarks>
    public void Abandon() {
        _body.Abandon();
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

        _body.Write(WebUtility.HtmlEncode(message));
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">The page passed null.</exception>
    public void WriteRaw(string message) {
        ArgumentNullException.ThrowIfNull(message);

        _body.Write(message);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// This render cannot stream, the page is a layout, it has redirected, or it is still wrapped in a
    /// layout.
    /// </exception>
    public async Task FlushAsync() {
        ThrowIfCannotStream();

        await _body.FlushAsync(ToResponse());
    }

    /// <summary>Writes whatever the page left buffered after its last flush.</summary>
    public Task CompleteAsync() => _body.CompleteAsync();

    // Validated before the commit: a redirect has no body, a layout cannot stream, and a page that
    // would be wrapped has no place to put the bytes before the layout has run.
    private void ThrowIfCannotStream() {
        if (_isLayout) {
            throw new InvalidOperationException($"{Path} is a layout; a layout is markup and cannot stream.");
        }

        if (!_body.CanStream) {
            throw new InvalidOperationException($"{Path} cannot stream its response in this render.");
        }

        if (_response.Location is not null) {
            throw new InvalidOperationException($"{Path} has redirected; a redirect has no body to stream.");
        }

        if (_wouldWrap?.Invoke(_response.ContentType) == true) {
            throw new InvalidOperationException(
                $"{Path} is wrapped in a layout, which needs its whole body before it can run. Set IPageLayout.Layout to null and write the page's own document before the first flush.");
        }
    }

    private void ThrowIfCommitted(string member) {
        if (_body.IsCommitted) {
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

        _response.SetStatus(status);
    }

    /// <inheritdoc/>
    public void SetContentType(string contentType) {
        ThrowIfCommitted(nameof(SetContentType));

        if (IgnoreAsLayout(nameof(SetContentType), contentType)) {
            return;
        }

        _response.SetContentType(contentType);
    }

    /// <inheritdoc/>
    public void SetHeader(string name, string value) {
        ThrowIfCommitted(nameof(SetHeader));

        if (IgnoreAsLayout(nameof(SetHeader), name)) {
            return;
        }

        _response.SetHeader(name, value);
    }

    /// <inheritdoc/>
    public void AllowScripts() {
        ThrowIfCommitted(nameof(AllowScripts));

        // The one response setting a layout shares: its script is part of its chrome, so the union
        // with the content's is what the response is served with.
        _response.AllowScripts();
    }

    /// <inheritdoc/>
    public void Redirect(string location, int status = 302) {
        ThrowIfCommitted(nameof(Redirect));

        if (IgnoreAsLayout(nameof(Redirect), location)) {
            return;
        }

        // The commit and layout gates are the context's, so the redirect is applied whole by the
        // state rather than through SetStatus, which would run those gates a second time.
        _response.Redirect(location, status);
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

            return _body.MintOnce(ref _antiforgeryField, _antiforgeryFactory);
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
}
