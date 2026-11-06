// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
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
        PageHealth? health = null) {
        Options = options ?? new RenderOptions();
        Budget = budget ?? new PageBudget(Options, new CompileOptions());
        Health = health ?? new PageHealth(Options.AbandonedLimit);
        Compiler = new RecordingCompiler(compiler ?? new PageCompiler());
        Metrics = new PageMetrics();

        Images = new PageImageBuilder(Compiler, Budget, Metrics, NullLogger<PageImageBuilder>.Instance);
        Renderer = new PageRenderer(storage, Compiler, Images, Metrics, Budget, Health, NullLogger<PageRenderer>.Instance);
        Editor = new PageEditor(storage, Images, Metrics, NullLogger<PageEditor>.Instance);
    }

    public RenderOptions Options { get; }

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
        int maxBodyChars = RenderOptions.DefaultMaxBodyChars) {
        PagePath key = Key(path);
        SimpleContext context = new(
            NullLoggerFactory.Instance,
            TestHttp.Request(key.Key),
            CancellationToken.None,
            new StringWriter(),
            EmptyPageStorage.Instance,
            DisabledPageFetcher.Instance,
            maxBodyChars);

        RenderResult result = await Renderer.RenderAsync(key, context, CancellationToken.None);

        return (result, context.GetBody());
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
