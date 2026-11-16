// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Work;

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
        DiffRenderer? diff = null,
        PageJobOptions? jobOptions = null,
        IWorkerStore? workerStore = null,
        TimeProvider? clock = null,
        PageSeeds? seeds = null) {
        State = state ?? new FakePageStateStore();
        Sessions = sessionStore ?? new FakePageSessionStore();
        Session = new PageSessionRequest(uk.osric.asp.Web.Authentication.PageSessionCookie.Mint(), null, presented: false, exists: false);
        Fetch = fetch ?? new FakePageFetcher();
        ReadStore = readStore ?? new FakePageReadStore();
        ContentTypes = contentTypes ?? TestContentTypes.Shipped;
        Markdown = markdown ?? new MarkdownRenderer();
        Diff = diff ?? new DiffRenderer();
        StorageOptions = storageOptions ?? TestOptions.Storage;
        FetchOptions = fetchOptions ?? TestOptions.Fetch;
        ReadOptions = TestOptions.Read;
        Options = options ?? TestOptions.FixtureRender;
        Budget = budget ?? new PageBudget(Options, TestOptions.FixtureCompile, TestOptions.Stream);
        Health = health ?? new PageHealth(Options.AbandonedLimit);
        Compiler = new RecordingCompiler(compiler ?? new PageCompiler());
        Metrics = new PageMetrics();
        Seeds = seeds ?? new PageSeeds(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds"));

        Storage = storage;
        Images = new PageImageBuilder(Compiler, Budget, Metrics, NullLogger<PageImageBuilder>.Instance);
        Libraries = new PageLibraries(ReadStore);
        Revisions = revisionStore ?? new FakePageRevisionStore(storage);
        Saver = new PageSaver(Revisions, Images, Libraries, Metrics, NullLogger<PageSaver>.Instance);
        Renderer = new PageRenderer(
            storage,
            Compiler,
            Images,
            Libraries,
            Metrics,
            Budget,
            Health,
            rendererLogger ?? NullLogger<PageRenderer>.Instance);
        Editor = new PageEditor(storage, Saver);

        JobOptions = jobOptions ?? TestOptions.Jobs;
        WorkerStore = workerStore ?? new FakeWorkerStore(storage);
        Reserved = new ReservedPaths(ReservedPaths.Default);
        JobQueue = new PageJobQueue(JobOptions);
        Jobs = new PageJobs(
            JobOptions, JobQueue, Reserved, ReadStore, clock ?? TimeProvider.System, NullLogger<PageJobs>.Instance);
        JobRunner = new PageJobRunner(
            JobQueue,
            Jobs,
            new PageJobEnvironment(
                JobOptions, WorkerStore, State, StorageOptions, Fetch, FetchOptions, ReadStore, Seeds, ReadOptions,
                Reserved, ContentTypes, Images, Libraries, Compiler, Metrics),
            NullLoggerFactory.Instance,
            NullLogger<PageJobRunner>.Instance);
    }

    public RenderOptions Options { get; }

    /// <summary>The libraries every compile in this harness may name, read from <see cref="ReadStore"/>.</summary>
    public PageLibraries Libraries { get; }

    /// <summary>Where every save in this harness is written.</summary>
    /// <summary>The pack this release ships, read the way the host reads it.</summary>
    public PageSeeds Seeds { get; }

    public FakePageStore Storage { get; }

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

    /// <summary>The job bounds this harness runs with.</summary>
    public PageJobOptions JobOptions { get; }

    /// <summary>Where a job reads its worker and stores a rebuilt image.</summary>
    public IWorkerStore WorkerStore { get; }

    /// <summary>The keys the site's own routes answer.</summary>
    public ReservedPaths Reserved { get; }

    /// <summary>The queue a job start is written to.</summary>
    public PageJobQueue JobQueue { get; }

    /// <summary>The jobs themselves.</summary>
    public PageJobs Jobs { get; }

    /// <summary>The runner that gives each job a scope and a load context.</summary>
    public PageJobRunner JobRunner { get; }

    /// <summary>Starts the runner, as the host's hosted service would.</summary>
    public Task StartJobsAsync() => JobRunner.StartAsync(CancellationToken.None);

    /// <summary>Stops the runner, which cancels the jobs it is running.</summary>
    public Task StopJobsAsync() => JobRunner.StopAsync(CancellationToken.None);

    public static PagePath Key(string path) {
        return PagePath.Normalize(path);
    }

    /// <summary>Installs the shipped seeds the way the host does, on a store a test can read.</summary>
    /// <remarks>
    /// The harness holds two stores where the host holds one, so a seeded library is mirrored into
    /// the read side as well: that is the copy every compile in this harness resolves a using from.
    /// </remarks>
    public async Task SeedAsync() {
        await PageAssetSeeder.SeedAsync(Storage, Seeds, CancellationToken.None);
        await MirrorSeededLibrariesAsync();
    }

    /// <summary>Makes the shipped libraries resolvable, for a test that stores its own templates.</summary>
    public async Task MirrorSeededLibrariesAsync() {
        foreach (SeedFile seed in Seeds.Files) {
            string template = seed.Source;

            PageKind kind = uk.osric.asp.Core.Compilation.TemplateSourceGenerator.Generate(template).Kind;
            if (kind is PageKind.Library) {
                ReadStore.Save(seed.Path.Key, template, templateKind: PageKind.Library);
            }
        }
    }

    public static PageTemplate Stored(FakePageStore storage, string path) {
        PageTemplate? page = storage[path];
        Assert.That(page, Is.Not.Null, $"{path} was not stored");

        // The assertion above proves the page exists.
        return page!;
    }

    public async Task<(RenderResult Result, string Output)> RenderAsync(
        string path,
        int? maxBodyChars = null,
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
        int? maxBodyChars = null,
        bool isHead = false,
        PageUser? user = null,
        RenderFetchBudget? budget = null,
        ReadBudget? readBudget = null,
        bool withFetch = true,
        bool withStorage = true,
        bool withCatalog = true,
        bool withWrites = true,
        bool withSession = true,
        string method = "GET") {
        PageCapabilities capabilities = new();

        if (withStorage) {
            PageStorageScope storage = new(path, State, StorageOptions, writable: !isHead);
            capabilities.Add<IPageStorage>(storage);
        }

        if (withSession) {
            PageSessionScope session = new(
                path, Sessions, TestOptions.Session, Session, writable: !isHead, slides: !isHead);
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
                ContentTypes,
                Seeds,
                Images.RuntimeId);
            capabilities.Add<IPageCatalog>(catalog);
        }

        if (withWrites) {
            PageWritesScope writes = new(path, Editor, user, writable: !isHead, Reserved, Seeds);
            capabilities.Add<IPageWrites>(writes);
        }

        return new SimpleContext(
            NullLoggerFactory.Instance,
            TestHttp.Request(path.Key, method),
            CancellationToken.None,
            new StringWriter(),
            capabilities,
            ContentTypes,
            maxBodyChars ?? TestOptions.Render.MaxBodyChars,
            TestOptions.Editor.Key,
            user);
    }

    /// <summary>Builds the environment the page endpoint renders against, as the host wires it.</summary>
    /// <param name="antiforgery">The token source the render mints its form field from.</param>
    /// <param name="editor">The key offered the editor capability; the default is `/pages`.</param>
    /// <param name="layout">The layout every code page is wrapped in.</param>
    /// <returns>The environment, sharing this harness's stores, budget and meters.</returns>
    public PageEnvironment CreateEnvironment(
        IAntiforgery antiforgery,
        PageEditorOptions? editor = null,
        string layout = "/_layout") {
        ArgumentNullException.ThrowIfNull(antiforgery);

        return new PageEnvironment(
            new ReservedPaths(ReservedPaths.Default),
            editor ?? TestOptions.Editor,
            TestOptions.Layout with { Default = layout },
            Options,
            ContentTypes,
            StorageOptions,
            FetchOptions,
            ReadOptions,
            State,
            ReadStore,
            Seeds,
            Saver,
            Fetch,
            Metrics,
            antiforgery,
            Markdown,
            Diff,
            TestOptions.Session,
            Sessions,
            TestSessions.Cookie,
            Jobs);
    }

    // The timeout tests measure the page, not the compiler: a page compiled on its first request
    // would spend the beginning of a short budget on a cold Roslyn and be dropped before it starts.
    public Task StoreCompiledAsync(string path, string content) {
        return Editor.SaveAsync(Key(path), content);
    }

    public void Dispose() {
        JobRunner.Dispose();
        Metrics.Dispose();
    }
}
