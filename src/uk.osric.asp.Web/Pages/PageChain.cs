// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;

/// <summary>One request's page and the layouts it is wrapped in.</summary>
/// <remarks>
/// <para>
/// The content renders first, because only then does the host know what it produced: a redirect or a
/// non-HTML response is never wrapped, and a page that cleared <see cref="IPageLayout.Layout"/> is
/// served alone. What follows is the chain, inside out — each layout wrapping the one below it — so
/// the outermost layer is the document that is written.
/// </para>
/// <para>
/// Every layer is a real render of a real page: its own key, and so its own storage handle, under
/// the request's path and the session's one slot, deadline and body budget.
/// </para>
/// </remarks>
internal sealed class PageChain {

    private readonly CancellationToken _aborted;
    private readonly Func<string> _antiforgeryField;
    private readonly BodyBudget _bodyBudget;
    private readonly PageEnvironment _environment;
    private readonly PageEditor? _editor;
    private readonly RenderFetchBudget _fetchBudget;
    private readonly bool _isHead;
    private readonly IReadOnlyDictionary<string, string> _assets;
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _loginRedirect;
    private readonly ReadBudget _readBudget;
    private readonly PageRequest _request;
    private readonly PageSessionRequest _session;
    private readonly PageUser? _user;

    /// <summary>Creates the chain for one request.</summary>
    /// <param name="environment">What the endpoint needs beyond the renderer.</param>
    /// <param name="loggerFactory">Creates the logger every layer writes through.</param>
    /// <param name="request">The request snapshot, shared by every layer.</param>
    /// <param name="user">The signed-in account, or <see langword="null"/>.</param>
    /// <param name="session">The visitor's session, resolved once by the endpoint.</param>
    /// <param name="isHead">Whether the request is a HEAD, which renders but never writes state.</param>
    /// <param name="bodyBudget">The allowance the whole chain shares.</param>
    /// <param name="fetchBudget">The fetch allowance the whole chain shares.</param>
    /// <param name="readBudget">The template allowance the whole chain shares.</param>
    /// <param name="assets">The live assets every layer's links may be versioned against.</param>
    /// <param name="loginRedirect">Where a guard sends an anonymous visitor.</param>
    /// <param name="antiforgeryField">
    /// Mints the antiforgery pair once per request, so every layer's form is verifiable against the
    /// same token.
    /// </param>
    /// <param name="aborted">The request's cancellation token, shared by every layer.</param>
    public PageChain(
        PageEnvironment environment,
        PageEditor? editor,
        ILoggerFactory loggerFactory,
        PageRequest request,
        PageUser? user,
        PageSessionRequest session,
        bool isHead,
        CancellationToken aborted,
        BodyBudget bodyBudget,
        RenderFetchBudget fetchBudget,
        ReadBudget readBudget,
        IReadOnlyDictionary<string, string> assets,
        string loginRedirect,
        Func<string> antiforgeryField) {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bodyBudget);
        ArgumentNullException.ThrowIfNull(fetchBudget);
        ArgumentNullException.ThrowIfNull(readBudget);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(antiforgeryField);

        _environment = environment;
        _editor = editor;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(SimpleContext.PageCategory);
        _request = request;
        _session = session;
        _user = user;
        _isHead = isHead;
        _aborted = aborted;
        _bodyBudget = bodyBudget;
        _fetchBudget = fetchBudget;
        _readBudget = readBudget;
        _assets = assets;
        _loginRedirect = loginRedirect;
        _antiforgeryField = antiforgeryField;
    }

    /// <summary>Builds the context one layer of the chain writes through.</summary>
    /// <param name="key">The layer's canonical key.</param>
    /// <param name="writer">The buffer the layer's body goes to.</param>
    /// <param name="layout">The layer's slots.</param>
    /// <param name="isLayout">Whether the layer is a layout, which may not touch the response.</param>
    public SimpleContext CreateLayer(PagePath key, TextWriter writer, PageLayoutState layout, bool isLayout) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(layout);

        PageStorageScope storage = new(key, _environment.StateStore, _environment.Storage, writable: !_isHead);
        PageSessionScope session = new(
            key,
            _environment.SessionStore,
            _environment.Sessions,
            _session,
            writable: !_isHead && !isLayout,
            slides: !_isHead);
        PageHttpFetch fetch = new(
            key,
            _isHead,
            _aborted,
            _environment.Fetcher,
            _environment.Fetch,
            _fetchBudget,
            _environment.Metrics,
            _loggerFactory.CreateLogger(SimpleContext.PageCategory));
        PageCatalogScope catalog = new(
            key,
            _environment.ReadStore,
            _environment.Read,
            _readBudget,
            _environment.Reserved,
            _environment.ContentTypes,
            _environment.Saver.RuntimeId);

        PageCapabilities capabilities = new PageCapabilities()
            .Add<IBuildInfo>(BuildInfo.Current)
            .Add<IPageStorage>(storage)
            .Add<IPageSession>(session)
            .Add<IHttpFetch>(fetch)
            .Add<IPageCatalog>(catalog)
            .Add<IPageLayout>(layout)
            .Add<IMarkdownRenderer>(_environment.Markdown)
            .Add<IDiffRenderer>(_environment.Diff);

        // A layout is markup, so it is offered the read and not the write: one that saved itself
        // would append a revision on every render it wraps (DEC-194).
        if (!isLayout) {
            capabilities.Add<IPageWriter>(new PageWriterScope(key, _environment.Saver, _user, writable: !_isHead));

            // The editor's authority is the registration, not the handle: no other key is offered it,
            // and a layout never is.
            if (_editor is { } editor && string.Equals(key.Key, PageEditorScope.EditorKey, StringComparison.Ordinal)) {
                capabilities.Add<IPageEditor>(new PageEditorScope(key, editor, _user, writable: !_isHead, _environment.Reserved));
            }
        }

        return new SimpleContext(
            _loggerFactory,
            _request,
            _aborted,
            writer,
            capabilities,
            _environment.ContentTypes,
            _environment.Render.MaxBodyChars,
            _user,
            _loginRedirect,
            _antiforgeryField,
            key,
            isLayout,
            _bodyBudget,
            href => PageAssetMap.Resolve(_assets, href));
    }

    /// <summary>Wraps the content in the layouts it names, outermost last.</summary>
    /// <param name="session">The render session the chain renders under.</param>
    /// <param name="path">The page the visitor asked for.</param>
    /// <param name="contentLayout">The content layer's slots.</param>
    /// <param name="response">What the content asked the response to be.</param>
    /// <param name="body">What the content wrote.</param>
    /// <returns>The body to write, or the error that says why the chain could not be built.</returns>
    public async Task<PageChainResult> WrapAsync(
        PageRenderer.RenderSession session,
        PagePath path,
        PageLayoutState contentLayout,
        PageResponse response,
        string body) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(contentLayout);
        ArgumentNullException.ThrowIfNull(response);

        // A redirect has no document to wrap, and a page that answers a stylesheet or a script is
        // not HTML at all. Both are served exactly as the content asked.
        if (response.Location is not null
            || !IsHtml(response.ContentType)
            || contentLayout.Layout is not { } target) {
            return new PageChainResult.Served(response, body);
        }

        if (contentLayout.TitleOrNull is null) {
            // Refused here rather than noticed by the layout, so the message names the page that
            // forgot instead of the layout that found nothing to write.
            return new PageChainResult.Failed(StatusCodes.Status500InternalServerError, new ErrorMessage(
                "page.layout_title_missing",
                "The page set no title",
                $"{path} is wrapped by a layout but set no title. Set one with ctx.Capabilities.GetRequired<IPageLayout>().Title, or clear Layout to write the page's own document.",
                ErrorMessage.DefaultRemedies(_user is not null)));
        }

        bool allowsScripts = response.AllowsScripts;
        PageLayoutState inner = contentLayout;
        string innerBody = body;
        int depth = 0;
        HashSet<string> seen = [path.Key];

        while (target is not null) {
            if (depth >= _environment.Render.MaxLayoutDepth) {
                _logger.LogWarning("Page {Path} named more than {Depth} layouts; the chain stopped at the limit",
                    path.Key, _environment.Render.MaxLayoutDepth);
                break;
            }

            PagePath key = PagePath.Normalize(target);

            if (!seen.Add(key.Key)) {
                // The layout names a page already in the chain - itself, or one of its children.
                // The default stopping itself is what makes the seeded layout need no special case,
                // so it is expected; any other repeat is a cycle somebody should know about.
                if (!string.Equals(_environment.Layout.Default, key.Key, StringComparison.Ordinal)) {
                    _logger.LogWarning("Page {Path} reaches layout {Layout} twice; the chain stopped at the repeat",
                        path.Key, key.Key);
                }

                break;
            }

            if (!key.IsAddressable || _environment.Reserved.IsReserved(key)) {
                return new PageChainResult.Failed(StatusCodes.Status500InternalServerError, new ErrorMessage(
                    "page.layout_unavailable",
                    "The layout is not a page",
                    $"{path} is wrapped in {key}, which is not a page this site can serve.",
                    ErrorMessage.DefaultRemedies(_user is not null)));
            }

            StringWriter layoutOutput = new();
            PageLayoutState layout = new(_environment.Layout.Default, inner) { Body = innerBody };
            SimpleContext context = CreateLayer(key, layoutOutput, layout, isLayout: true);

            RenderResult rendered = await session.RenderAsync(key, context);

            if (rendered.Status != RenderStatus.Rendered) {
                // A default that will not serve degrades to serving the page unwrapped rather than
                // taking the site down; a layout the page chose is an error, because silence would be worse.
                if (string.Equals(_environment.Layout.Default, key.Key, StringComparison.Ordinal)) {
                    _environment.Metrics.RecordLayoutFallback(path);
                    _logger.LogWarning(
                        "Default layout {Layout} could not be served for {Path} ({Status}); the page was served unwrapped",
                        key.Key, path.Key, rendered.Status);
                    break;
                }

                return new PageChainResult.Failed(StatusCodes.Status500InternalServerError, new ErrorMessage(
                    "page.layout_unavailable",
                    "The layout could not be served",
                    $"{path} is wrapped in {key.Key}, which could not be served ({rendered.Status}).",
                    ErrorMessage.DefaultRemedies(_user is not null)));
            }

            allowsScripts |= context.ToResponse().AllowsScripts;
            innerBody = context.GetBody();
            inner = layout;
            target = layout.Layout;
            depth++;
        }

        return new PageChainResult.Served(response with { AllowsScripts = allowsScripts }, innerBody);
    }

    private static bool IsHtml(string contentType) =>
        contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
}
