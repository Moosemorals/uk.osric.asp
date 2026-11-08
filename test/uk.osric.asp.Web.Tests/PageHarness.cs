// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
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
        PageStorageOptions? storageOptions = null,
        ILogger<PageRenderer>? rendererLogger = null,
        PageFetchOptions? fetchOptions = null,
        PageContentTypes? contentTypes = null) {
        State = state ?? new FakePageStateStore();
        Fetch = fetch ?? new FakePageFetcher();
        ContentTypes = contentTypes ?? TestContentTypes.Shipped;
        StorageOptions = storageOptions ?? new PageStorageOptions();
        FetchOptions = fetchOptions ?? new PageFetchOptions();
        Options = options ?? new RenderOptions();
        Budget = budget ?? new PageBudget(Options, new CompileOptions());
        Health = health ?? new PageHealth(Options.AbandonedLimit);
        Compiler = new RecordingCompiler(compiler ?? new PageCompiler());
        Metrics = new PageMetrics();

        Images = new PageImageBuilder(Compiler, Budget, Metrics, NullLogger<PageImageBuilder>.Instance);
        Renderer = new PageRenderer(
            storage,
            Compiler,
            Images,
            Metrics,
            Budget,
            Health,
            rendererLogger ?? NullLogger<PageRenderer>.Instance);
        Editor = new PageEditor(storage, Images, Metrics, NullLogger<PageEditor>.Instance);
    }

    public RenderOptions Options { get; }

    /// <summary>The page state every render in this harness shares.</summary>
    public FakePageStateStore State { get; }

    /// <summary>Where every fetch in this harness goes.</summary>
    public FakePageFetcher Fetch { get; }

    /// <summary>The content-type map every context in this harness renders against.</summary>
    public PageContentTypes ContentTypes { get; }

    public PageStorageOptions StorageOptions { get; }

    public PageFetchOptions FetchOptions { get; }

    public PageBudget Budget { get; }

    public PageHealth Health { get; }

    public RecordingCompiler Compiler { get; }

    public PageMetrics Metrics { get; }

    public PageImageBuilder Images { get; }

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
        bool withStorage = true) {
        PagePath key = Key(path);
        SimpleContext context = CreateContext(
            key, maxBodyChars, isHead, user, withFetch: withFetch, withStorage: withStorage);

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
        bool withFetch = true,
        bool withStorage = true) {
        PageFeatures features = new();

        if (withStorage) {
            PageStorageScope storage = new(path, State, StorageOptions, writable: !isHead);
            features.Add<IPageStorage>(storage);
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
            features.Add<IHttpFetch>(fetch);
        }

        return new SimpleContext(
            NullLoggerFactory.Instance,
            TestHttp.Request(path.Key, isHead ? "HEAD" : "GET"),
            CancellationToken.None,
            new StringWriter(),
            features,
            ContentTypes,
            maxBodyChars,
            user);
    }

    // The timeout tests measure the page, not the compiler: a page compiled on its first request
    // spends part of a one-second budget on a cold Roslyn and would be dropped before it starts.
    public Task StoreCompiledAsync(string path, string content) {
        return Editor.SaveAsync(Key(path), content);
    }

    public void Dispose() {
        Metrics.Dispose();
    }
}
