// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>The only <see cref="IContext"/> a page ever runs against.</summary>
/// <remarks>
/// <para>
/// The request side of the contract: what the page can read and ask for, and the wiring that gives it
/// a <see cref="SimpleResponse"/> to write into. The rules about the response live there.
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

    private readonly Func<string, string>? _assetUrl;
    private readonly PageCapabilities _capabilities;
    private readonly string _editorPath;
    private readonly ILogger _logger;
    private readonly PagePath _path;
    private readonly PageRequest _request;

    /// <summary>The response this layer writes into.</summary>
    public SimpleResponse Response { get; }

    IPageResponse IContext.Response => Response;

    /// <summary>Creates the context a page runs against.</summary>
    /// <param name="loggerFactory">Creates the one logger every page writes through.</param>
    /// <param name="request">The request snapshot.</param>
    /// <param name="aborted">The token from the request being served.</param>
    /// <param name="writer">The buffer the body is written to.</param>
    /// <param name="capabilities">The capabilities this layer offers.</param>
    /// <param name="stores">The page and visitor subjects this layer writes to.</param>
    /// <param name="contentTypes">The media type the page's key names.</param>
    /// <param name="maxBodyChars">How much body this layer may write when no chain budget is given.</param>
    /// <param name="user">The signed-in account, or <see langword="null"/> for an anonymous visitor.</param>
    /// <param name="loginRedirect">Where <see cref="SimpleResponse.RedirectToLogin"/> sends an anonymous visitor.</param>
    /// <param name="antiforgeryField">
    /// Builds the hidden antiforgery input, or <see langword="null"/> when the page must get nothing.
    /// </param>
    /// <param name="path">The layer's own key, which is the request's path for the content layer.</param>
    /// <param name="role">
    /// What this layer is. Only the content owns the response: a layout or an embedded page is
    /// markup, so status, content type and headers are ignored, and an embedded page's redirect
    /// refuses because it has no response to redirect.
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
        IPageStores stores,
        PageContentTypes contentTypes,
        int maxBodyChars,
        string editorPath,
        PageUser? user = null,
        string loginRedirect = "/login",
        Func<string>? antiforgeryField = null,
        PagePath? path = null,
        PageLayerRole role = PageLayerRole.Content,
        BodyBudget? bodyBudget = null,
        Func<string, string>? assetUrl = null,
        PageCommitState? commitState = null,
        IPageBodySink? bodySink = null,
        Func<string, bool>? wouldWrap = null) {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(contentTypes);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginRedirect);
        ArgumentException.ThrowIfNullOrWhiteSpace(editorPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBodyChars, 1);

        // The path is canonical here: PageEndpoint redirects anything else before building a context.
        _logger = loggerFactory.CreateLogger(PageCategory);
        _path = path ?? PagePath.Normalize(request.Path);
        _request = request;
        _editorPath = editorPath;
        _assetUrl = assetUrl;
        Aborted = aborted;
        _capabilities = capabilities;
        Stores = stores;
        User = user;

        // The budget and the commit are shared by the whole chain; a layer on its own gets one of each.
        PageBody body = new(
            writer,
            _path,
            bodyBudget ?? new BodyBudget(maxBodyChars),
            commitState ?? new PageCommitState(),
            bodySink,
            aborted);

        // The extension decides the initial content type; SetContentType overrides it.
        Response = new SimpleResponse(
            new PageResponseState(contentTypes.ForPath(_path.Key)),
            body,
            _path,
            request,
            loginRedirect,
            role,
            _logger,
            antiforgeryField,
            wouldWrap);
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

    /// <inheritdoc/>
    public IPageStores Stores { get; }

    /// <summary>Gives the page the render's lifetime, so its I/O and its stream share one clock.</summary>
    /// <param name="lifetime">The lifetime the renderer opened for this request.</param>
    public void AttachLifetime(RenderLifetime lifetime) {
        ArgumentNullException.ThrowIfNull(lifetime);

        Response.AttachLifetime(lifetime);
        _capabilities.AttachLifetime(lifetime);

        if (Stores is IRenderBoundCapability bound) {
            bound.AttachDeadline(lifetime.Token);
        }
    }

    /// <summary>Stops the page writing, once its render has been given up on.</summary>
    /// <remarks>
    /// The page cannot be stopped, so this is what keeps it from filling the buffer — or
    /// racing the response — after the site has answered without it.
    /// </remarks>
    public void Abandon() {
        Response.Abandon();
        _capabilities.Abandon();

        if (Stores is IRenderBoundCapability bound) {
            bound.Abandon();
        }
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
    public string AssetUrl(string href) {
        ArgumentNullException.ThrowIfNull(href);

        return _assetUrl?.Invoke(href) ?? href;
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
}
