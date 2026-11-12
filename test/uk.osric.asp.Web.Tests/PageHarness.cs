// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>Builds the page classes over one storage and one counting compiler.</summary>
/// <remarks>
/// The three classes share the budget, the health count and the meter exactly as the host wires them,
/// so a test can save through one and render through another without guessing at the wiring.
/// </remarks>
internal sealed class PageHarness : IDisposable {

    public PageHarness(
        FakePageStore storage,
        IPageCompiler? compiler = null,
        RenderOptions? options = null,
        PageBudget? budget = null,
        PageHealth? health = null,
        FakePageStateStore? state = null,
        FakePageFetcher? fetch = null,
        FakePageReadStore? readStore = null,
        FakePageRevisionStore? revisionStore = null,
        FakePageSessionStore? sessionStore = null,
        PageStorageOptions? storageOptions = null,
        ILogger<PageRenderer>? rendererLogger = null,
        PageFetchOptions? fetchOptions = null,
        PageContentTypes? contentTypes = null,
        MarkdownRenderer? markdown = null,
        DiffRenderer? diff = null) {
        State = state ?? new FakePageStateStore();
        Sessions = sessionStore ?? new FakePageSessionStore();
        Session = new PageSessionRequest(uk.osric.asp.Web.Authentication.PageSessionCookie.Mint(), null, presented: false, exists: false);
        Fetch = fetch ?? new FakePageFetcher();
        ReadStore = readStore ?? new FakePageReadStore();
        ContentTypes = contentTypes ?? TestContentTypes.Shipped;
        Markdown = markdown ?? new MarkdownRenderer();
        Diff = diff ?? new DiffRenderer();
        StorageOptions = storageOptions ?? new PageStorageOptions();
        FetchOptions = fetchOptions ?? new PageFetchOptions();
        ReadOptions = new PageReadOptions();
        Options = options ?? new RenderOptions();
        Budget = budget ?? new PageBudget(Options, new CompileOptions());
        Health = health ?? new PageHealth(Options.AbandonedLimit);
        Compiler = new RecordingCompiler(compiler ?? new PageCompiler());
        Metrics = new PageMetrics();

        Images = new PageImageBuilder(Compiler, Budget, Metrics, NullLogger<PageImageBuilder>.Instance);
        Revisions = revisionStore ?? new FakePageRevisionStore(storage);
        Saver = new PageSaver(Revisions, Images, Metrics, NullLogger<PageSaver>.Instance);
        Renderer = new PageRenderer(
            storage,
            Compiler,
            Images,
            Metrics,
            Budget,
            Health,
            rendererLogger ?? NullLogger<PageRenderer>.Instance);
        Editor = new PageEditor(storage, Images, Saver);
    }

    public RenderOptions Options { get; }

    /// <summary>The page state every render in this harness shares.</summary>
    public FakePageStateStore State { get; }

    /// <summary>The session state every render in this harness shares.</summary>
    public FakePageSessionStore Sessions { get; }

    /// <summary>The request's session, shared by every layer of a render.</summary>
    public PageSessionRequest Session { get; }

    /// <summary>Where every fetch in this harness goes.</summary>
    public FakePageFetcher Fetch { get; }

    /// <summary>Where every catalog read in this harness goes.</summary>
    public FakePageReadStore ReadStore { get; }

    /// <summary>The caps every catalog read in this harness uses.</summary>
    public PageReadOptions ReadOptions { get; }

    /// <summary>The content-type map every context in this harness renders against.</summary>
    public PageContentTypes ContentTypes { get; }

    /// <summary>The Markdown renderer every context in this harness offers.</summary>
    public MarkdownRenderer Markdown { get; }

    /// <summary>The diff renderer every context in this harness offers.</summary>
    public DiffRenderer Diff { get; }

    public PageStorageOptions StorageOptions { get; }

    public PageFetchOptions FetchOptions { get; }

    public PageBudget Budget { get; }

    public PageHealth Health { get; }

    public RecordingCompiler Compiler { get; }

    public PageMetrics Metrics { get; }

    public PageImageBuilder Images { get; }

    /// <summary>Where every save in this harness is written.</summary>
    public FakePageRevisionStore Revisions { get; }

    /// <summary>The one save path every caller in this harness shares.</summary>
    public PageSaver Saver { get; }

    public PageRenderer Renderer { get; }

    public PageEditor Editor { get; }

    public static PagePath Key(string path) {
        return PagePath.Normalize(path);
    }

    public static PageTemplate Stored(FakePageStore storage, string path) {
        PageTemplate? page = storage[path];
        Assert.That(page, Is.Not.Null, $"{path} was not stored");

        // The assertion above proves the page exists.
        return page!;
    }

    public async Task<(RenderResult Result, string Output)> RenderAsync(
        string path,
        int maxBodyChars = RenderOptions.DefaultMaxBodyChars,
        bool isHead = false,
        PageUser? user = null,
        bool withFetch = true,
        bool withStorage = true,
        bool withSession = true) {
        PagePath key = Key(path);
        SimpleContext context = CreateContext(
            key, maxBodyChars, isHead, user, withFetch: withFetch, withStorage: withStorage, withSession: withSession);

        RenderResult result = await Renderer.RenderAsync(key, context, CancellationToken.None);

        return (result, context.GetBody());
    }

    /// <summary>Builds one layer's context, with or without the capabilities the host normally adds.</summary>
    public SimpleContext CreateContext(
        PagePath path,
        int maxBodyChars = RenderOptions.DefaultMaxBodyChars,
        bool isHead = false,
        PageUser? user = null,
        RenderFetchBudget? budget = null,
        ReadBudget? readBudget = null,
        bool withFetch = true,
        bool withStorage = true,
        bool withCatalog = true,
        bool withWriter = true,
        bool withSession = true,
        string method = "GET") {
        PageCapabilities capabilities = new();

        if (withStorage) {
            PageStorageScope storage = new(path, State, StorageOptions, writable: !isHead);
            capabilities.Add<IPageStorage>(storage);
        }

        if (withSession) {
            PageSessionScope session = new(
                path, Sessions, new PageSessionOptions(), Session, writable: !isHead, slides: !isHead);
            capabilities.Add<IPageSession>(session);
        }

        if (withFetch) {
            PageHttpFetch fetch = new(
                path,
                isHead,
                CancellationToken.None,
                Fetch,
                FetchOptions,
                budget ?? new RenderFetchBudget(FetchOptions.MaxRequestsPerRender),
                Metrics,
                NullLoggerFactory.Instance.CreateLogger(SimpleContext.PageCategory));
            capabilities.Add<IHttpFetch>(fetch);
        }

        capabilities.Add<IMarkdownRenderer>(Markdown);
        capabilities.Add<IDiffRenderer>(Diff);

        if (withCatalog) {
            PageCatalogScope catalog = new(
                path,
                ReadStore,
                ReadOptions,
                readBudget ?? new ReadBudget(ReadOptions.MaxSourceCharsPerRender),
                new ReservedPaths(ReservedPaths.Default),
                ContentTypes);
            capabilities.Add<IPageCatalog>(catalog);
        }

        if (withWriter) {
            PageWriterScope writer = new(path, Saver, user, writable: !isHead);
            capabilities.Add<IPageWriter>(writer);
        }

        return new SimpleContext(
            NullLoggerFactory.Instance,
            TestHttp.Request(path.Key, method),
            CancellationToken.None,
            new StringWriter(),
            capabilities,
            ContentTypes,
            maxBodyChars,
            user);
    }

    // The timeout tests measure the page, not the compiler: a page compiled on its first request
    // would spend the beginning of a short budget on a cold Roslyn and be dropped before it starts.
    public Task StoreCompiledAsync(string path, string content) {
        return Editor.SaveAsync(Key(path), content);
    }

    public void Dispose() {
        Metrics.Dispose();
    }
}
