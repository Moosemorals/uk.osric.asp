// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;

/// <summary>One render: the page the visitor asked for, the pages that wrap it, and the pages it includes.</summary>
/// <remarks>
/// <para>
/// A render is a tree. The page's body and the wrappers around it are one spine, and every layer may
/// embed, which is a child render with a spine of its own. The host walks it by making the calls, and
/// the ancestor stack is the path down it: the chain refuses to enter the same page with the same
/// request twice, which is what bounds a page that composes itself.
/// </para>
/// <para>
/// Every layer is a real render of a real page: its own key, and so its own storage handle, under the
/// request's path and the session's one slot, deadline and body budget.
/// </para>
/// </remarks>
internal sealed class PageChain {

    private readonly CancellationToken _aborted;
    private readonly Func<string> _antiforgeryField;
    private readonly IPageBodySink _bodySink;
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
    private readonly PageRenderer.RenderSession _session;
    private readonly PageSessionRequest _sessionRequest;
    private readonly PageUser? _user;
    private readonly PageWriteAction _writeAction = new();
    private int _embeds;

    /// <summary>Creates the chain for one request.</summary>
    /// <param name="environment">What the endpoint needs beyond the renderer.</param>
    /// <param name="editor">
    /// The page store's writer, when the host has one; every layer that is not a layout is offered it,
    /// and a host without one offers no write at all.
    /// </param>
    /// <param name="loggerFactory">Creates the logger every layer writes through.</param>
    /// <param name="request">The request snapshot, shared by every layer.</param>
    /// <param name="user">The signed-in account, or <see langword="null"/>.</param>
    /// <param name="session">The visitor's session, resolved once by the endpoint.</param>
    /// <param name="isHead">Whether the request is a HEAD, which renders but never writes state.</param>
    /// <param name="bodyBudget">The allowance the whole render shares.</param>
    /// <param name="bodySink">Where a flushing content layer writes its response.</param>
    /// <param name="fetchBudget">The fetch allowance the whole render shares.</param>
    /// <param name="readBudget">The template allowance the whole render shares.</param>
    /// <param name="assets">The live assets every layer's links may be versioned against.</param>
    /// <param name="loginRedirect">Where a guard sends an anonymous visitor.</param>
    /// <param name="antiforgeryField">
    /// Mints the antiforgery pair once per request, so every layer's form is verifiable against the
    /// same token.
    /// </param>
    /// <param name="render">
    /// The render session every layer runs under. It is opened before the content layer is built, so
    /// an embed holds the session it belongs to.
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
        BodyBudget bodyBudget,
        IPageBodySink bodySink,
        RenderFetchBudget fetchBudget,
        ReadBudget readBudget,
        IReadOnlyDictionary<string, string> assets,
        string loginRedirect,
        Func<string> antiforgeryField,
        PageRenderer.RenderSession render,
        CancellationToken aborted) {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bodyBudget);
        ArgumentNullException.ThrowIfNull(bodySink);
        ArgumentNullException.ThrowIfNull(fetchBudget);
        ArgumentNullException.ThrowIfNull(readBudget);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(antiforgeryField);
        ArgumentNullException.ThrowIfNull(render);

        _environment = environment;
        _editor = editor;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(SimpleContext.PageCategory);
        _request = request;
        _sessionRequest = session;
        _user = user;
        _isHead = isHead;
        _aborted = aborted;
        _bodyBudget = bodyBudget;
        _bodySink = bodySink;
        _fetchBudget = fetchBudget;
        _readBudget = readBudget;
        _assets = assets;
        _loginRedirect = loginRedirect;
        _antiforgeryField = antiforgeryField;
        _session = render;
    }

    /// <summary>The page a bare <see cref="IPageComposition.Wrap()"/> names, or <see langword="null"/>.</summary>
    public PagePath? DefaultLayout =>
        _environment.Layout.Default is { } key ? PagePath.Normalize(key) : null;

    /// <summary>Whether a key belongs to the shell rather than to the page store.</summary>
    /// <param name="key">The canonical key.</param>
    public bool IsReserved(PagePath key) => _environment.Reserved.IsReserved(key);

    /// <summary>Builds one layer of the render.</summary>
    /// <param name="key">The layer's canonical key.</param>
    /// <param name="role">What the layer is, which decides what it may do.</param>
    /// <param name="request">The request the layer runs against; its path is still the visitor's.</param>
    /// <param name="budget">The allowance this layer's writes are charged to.</param>
    /// <param name="path">The chain down to and including this layer.</param>
    /// <param name="depth">How many embeds sit above this layer.</param>
    /// <param name="content">The layer inside this one, when this layer wraps another.</param>
    public PageLayer CreateLayer(
        PagePath key,
        PageLayerRole role,
        PageRequest request,
        BodyBudget budget,
        IReadOnlyList<PageRenderIdentity> path,
        int depth,
        PageCapture? content = null) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(path);

        PageStorageScope storage = new(key, _environment.StateStore, _environment.Storage, writable: !_isHead);
        PageCommitState commit = new();
        PageSessionScope session = new(
            key,
            _environment.SessionStore,
            _environment.Sessions,
            _sessionRequest,
            writable: !_isHead && role != PageLayerRole.Layout,
            slides: !_isHead,
            commit: commit);
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
            _environment.Seeds,
            _environment.Saver.RuntimeId);

        PageLayoutState layout = new() { Content = content };
        PageCompositionState composition = new();

        // The scope writes an embed through this layer's response, and the response is built with the
        // capabilities the scope is one of: the closure is resolved once the page could call it.
        SimpleResponse? response = null;
        PageCompositionScope compositor = new(this, composition, request, path, depth, key, () => response!);

        PageCapabilities capabilities = new PageCapabilities()
            .Add<IBuildInfo>(BuildInfo.Current)
            .Add<IPageStorage>(storage)
            .Add<IPageSession>(session)
            .Add<IHttpFetch>(fetch)
            .Add<IPageCatalog>(catalog)
            .Add<IPageLayout>(layout)
            .Add<IMarkdownRenderer>(_environment.Markdown)
            .Add<IDiffRenderer>(_environment.Diff)
            .Add<IPageComposition>(compositor);

        // A layout is markup, so it is offered the read and not the write: one that saved itself
        // would append a revision on every render it wraps (`pages-layouts-are-markup`).
        if (role != PageLayerRole.Layout) {
            // A job is started by the page that can read its events, and never by a layout: a layout
            // that started one would start it once per page it wraps.
            if (_environment.Jobs is { } jobs) {
                capabilities.Add<IPageJobs>(new PageJobsScope(
                    key, _isHead, _user, jobs, jobs.Options));
            }

            // Every layer that is not a layout, whatever its key: the visitor is the authority, not
            // the page, and one render writes once however many layers it has.
            if (_editor is { } editor) {
                capabilities.Add<IPageWrites>(new PageWritesScope(
                    key, editor, _user, writable: !_isHead, _environment.Reserved, _environment.Seeds, _writeAction));
            }
        }

        SimpleContext context = new(
            _loggerFactory,
            request,
            _aborted,
            new StringWriter(),
            capabilities,
            _environment.ContentTypes,
            _environment.Render.MaxBodyChars,
            _environment.Editor.Key,
            _user,
            _loginRedirect,
            _antiforgeryField,
            key,
            role,
            budget,
            href => PageAssetMap.Resolve(_assets, href),
            commit,
            role == PageLayerRole.Content ? _bodySink : null,
            contentType => role == PageLayerRole.Content && IsHtml(contentType) && !composition.IsEmpty);

        response = context.Response;

        return new PageLayer(context, layout, composition, path, depth);
    }

    /// <summary>Wraps the content in the pages it declared, outermost last.</summary>
    /// <param name="path">The page the visitor asked for.</param>
    /// <param name="content">The content layer, after it ran.</param>
    /// <param name="response">What the content asked the response to be.</param>
    /// <returns>The body to write, or the error that says why the chain could not be built.</returns>
    public async Task<PageChainResult> WrapAsync(PagePath path, PageLayer content, PageResponse response) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(response);

        string body = content.Context.Response.GetBody();

        // A redirect has no document to wrap, and a page that answers a stylesheet or a script is not
        // HTML at all. Both are served exactly as the content asked.
        if (response.Location is not null || !IsHtml(response.ContentType) || content.Composition.IsEmpty) {
            return new PageChainResult.Served(response, body);
        }

        PageCapture capture = new(body, new PageFrame(
            content.Layout.Title, content.Layout.Head, content.Layout.Stylesheets, response.AllowsScripts));

        try {
            PageCapture outer = await RunWrapsAsync(content.Path, content.Depth, capture, content.Composition.Wraps);

            return new PageChainResult.Served(response with { AllowsScripts = outer.AllowsScripts }, outer.Body);
        } catch (PageChainFailureException failure) {
            return new PageChainResult.Failed(failure.StatusCode, failure.Error);
        }
    }

    /// <summary>Renders a page inside another one, wrappers and all, and returns what it produced.</summary>
    /// <param name="key">The embedded page's canonical key.</param>
    /// <param name="request">The request it runs against.</param>
    /// <param name="path">The chain that asked for it, which the recursion check reads.</param>
    /// <param name="depth">How many embeds already sit above the layer that asked.</param>
    /// <exception cref="InvalidOperationException">The page repeats in the chain, or a cap was reached.</exception>
    /// <exception cref="UserFacingException">The page, or a page wrapping it, cannot be served.</exception>
    /// <exception cref="OperationCanceledException">The render's deadline fired while it ran.</exception>
    public async Task<PageCapture> ComposeAsync(
        PagePath key,
        PageRequest request,
        IReadOnlyList<PageRenderIdentity> path,
        int depth) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(path);

        PageRenderIdentity identity = PageRenderIdentity.Of(key, request);
        CheckAncestors(key, identity, path);

        if (depth >= _environment.Render.MaxEmbedDepth) {
            throw new InvalidOperationException(
                $"{key.Key} would be embedded more than {_environment.Render.MaxEmbedDepth} deep; Pages:Render:MaxEmbedDepth stops the chain.");
        }

        if (Interlocked.Increment(ref _embeds) > _environment.Render.MaxEmbeds) {
            throw new InvalidOperationException(
                $"{key.Key} is one embed past the {_environment.Render.MaxEmbeds} this render may make; Pages:Render:MaxEmbeds stops the chain.");
        }

        IReadOnlyList<PageRenderIdentity> chain = [.. path, identity];
        PageLayer layer = CreateLayer(
            key,
            PageLayerRole.Embedded,
            request,
            new BodyBudget(Math.Max(_bodyBudget.Remaining, 1)),
            chain,
            depth + 1);

        RenderResult rendered = await _session.RenderAsync(key, layer.Context);

        if (rendered.Status != RenderStatus.Rendered) {
            throw Failure(key, rendered);
        }

        PageCapture capture = Capture(layer, allowsScripts: layer.Context.Response.ToResponse().AllowsScripts);

        try {
            return await RunWrapsAsync(chain, depth + 1, capture, layer.Composition.Wraps);
        } catch (PageChainFailureException failure) {
            throw new UserFacingException(failure.Error.Summary);
        }
    }

    internal static bool IsHtml(string contentType) =>
        contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

    // The wrappers a layer declared, each outside the last. A wrapper's own declarations go outside
    // it and inside whatever the caller declared next, so `Wrap("/_md"); Wrap()` is markdown first
    // and the site layout outside it.
    private async Task<PageCapture> RunWrapsAsync(
        IReadOnlyList<PageRenderIdentity> path,
        int depth,
        PageCapture inner,
        IReadOnlyList<PageWrap> declarations) {
        List<PageWrap> pending = [.. declarations];
        List<PageRenderIdentity> chain = [.. path];
        PageCapture capture = inner;
        int count = 0;

        while (pending.Count > 0) {
            PageWrap wrap = pending[0];
            pending.RemoveAt(0);

            if (count >= _environment.Render.MaxLayoutDepth) {
                _logger.LogWarning("Page {Path} named more than {Depth} layouts; the chain stopped at the limit",
                    path[0].Key, _environment.Render.MaxLayoutDepth);
                break;
            }

            PageRenderIdentity identity = PageRenderIdentity.Of(wrap.Key, wrap.Request);
            CheckAncestors(wrap.Key, identity, chain);

            IReadOnlyList<PageRenderIdentity> layerPath = [.. chain, identity];
            PageLayer layer = CreateLayer(
                wrap.Key, PageLayerRole.Layout, wrap.Request, _bodyBudget, layerPath, depth, capture);

            RenderResult rendered = await _session.RenderAsync(wrap.Key, layer.Context);

            // A wrapper must have code: a no-code page has no way to read Content, so its stored
            // source would replace the page instead of wrapping it.
            if (rendered.Status != RenderStatus.Rendered || rendered.IsSource) {
                // A default that will not serve degrades to serving the page unwrapped rather than
                // taking the site down; a wrapper the page named is an error, because silence is worse.
                if (wrap.IsDefault) {
                    _environment.Metrics.RecordLayoutFallback(PagePath.Normalize(path[0].Key));
                    _logger.LogWarning(
                        "Default layout {Layout} could not be served for {Path} ({Status}); the page was served unwrapped",
                        wrap.Key.Key, path[0].Key, rendered.Status);
                    break;
                }

                throw new PageChainFailureException(StatusCodes.Status500InternalServerError, new ErrorMessage(
                    "page.layout_unavailable",
                    "The layout could not be served",
                    $"{path[0].Key} is wrapped in {wrap.Key.Key}, which could not be served ({rendered.Status}).",
                    ErrorMessage.DefaultRemedies(_user is not null, _environment.Editor.Key)));
            }

            PageResponse response = layer.Context.Response.ToResponse();
            capture = Capture(layer, allowsScripts: response.AllowsScripts || capture.AllowsScripts);

            chain.Add(identity);
            pending.InsertRange(0, layer.Composition.Wraps);
            count++;
        }

        return capture;
    }

    // The frame is the layer's own slots; the one thing the host still composes is the script
    // permission, so a document that carries a body cannot lose the permission its script needs.
    private static PageCapture Capture(PageLayer layer, bool allowsScripts) => new(
        layer.Context.Response.GetBody(),
        new PageFrame(layer.Layout.Title, layer.Layout.Head, layer.Layout.Stylesheets, allowsScripts));

    // The same page with the same request in an ancestor is a cycle. The same page with a different
    // request is a recursive component: allowed, capped and counted, so the one page doing it is
    // visible without making the ordinary case noisy.
    private void CheckAncestors(PagePath key, PageRenderIdentity identity, IReadOnlyList<PageRenderIdentity> chain) {
        bool recursive = false;

        foreach (PageRenderIdentity ancestor in chain) {
            if (!string.Equals(ancestor.Key, identity.Key, StringComparison.Ordinal)) {
                continue;
            }

            if (ancestor == identity) {
                throw new InvalidOperationException(
                    $"{key.Key} is already being rendered in this request with the same request; a page cannot compose itself in a loop.");
            }

            recursive = true;
        }

        if (recursive) {
            _environment.Metrics.RecordRecursiveComposition(key);
            _logger.LogDebug("Page {Path} composes itself with a different request; the caps bound it", key.Key);
        }
    }

    // A page that cannot be composed is the page's problem to explain, so every case is a message
    // naming the key. A timed-out render is the render's cancellation, so the request answers 504
    // rather than looking like a page fault.
    private static Exception Failure(PagePath key, RenderResult rendered) => rendered.Status switch {
        RenderStatus.Timeout => new OperationCanceledException(
            $"{key} is included in another page and outran the render's budget."),
        RenderStatus.NotFound => new UserFacingException(
            $"{key} is included in another page, but no page is stored there."),
        _ => new UserFacingException(
            $"{key} is included in another page but could not be compiled."),
    };
}
