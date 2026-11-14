// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

/// <summary>Builds the import components over fakes, wired as the host wires them.</summary>
internal sealed class ImportHarness : IDisposable {

    public ImportHarness(
        IPageCompiler? compiler = null,
        ArchiveOptions? options = null,
        PageStorageOptions? storage = null) {
        Options = options ?? TestOptions.Archive;
        Storage = storage ?? TestOptions.Storage;
        Compiler = new RecordingCompiler(compiler ?? new PageCompiler());
        Metrics = new PageMetrics();

        Images = new PageImageBuilder(
            Compiler,
            new PageBudget(TestOptions.Render, TestOptions.Compile),
            Metrics,
            NullLogger<PageImageBuilder>.Instance);
        Processor = new ImportProcessor(
            Images, Batches, new PageLibraries(ReadStore), Metrics, Options, NullLogger<ImportProcessor>.Instance);
        Stages = new ImportStageStore(Options, Clock);
        Queue = new ImportQueue(Options);
        Reader = new PageArchiveReader(Options, Storage, Reserved, NullLogger<PageArchiveReader>.Instance);
    }

    public ArchiveOptions Options { get; }

    public PageStorageOptions Storage { get; }

    public RecordingCompiler Compiler { get; }

    public PageMetrics Metrics { get; }

    public PageImageBuilder Images { get; }

    /// <summary>The libraries every compile in this harness may name.</summary>
    public FakePageReadStore ReadStore { get; } = new();

    public FakePageStore Pages { get; } = new();

    public FakePageStateStore State { get; } = new();

    public FakeBatchStore Batches { get; } = new();

    public ImportProcessor Processor { get; }

    public ImportStageStore Stages { get; }

    public ImportQueue Queue { get; }

    public PageArchiveReader Reader { get; }

    public TestClock Clock { get; } = new();

    public ReservedPaths Reserved { get; } = new(ReservedPaths.Default);

    /// <summary>Stages an archive as an approved upload would.</summary>
    /// <param name="pages">The pages the archive carries.</param>
    public ImportStage Stage(params (string Path, string Source, IReadOnlyList<PageStateValue> State)[] pages) {
        PageArchiveEntry[] entries = [.. pages.Select(page => new PageArchiveEntry(
            PagePath.Normalize(page.Path), page.Source, page.State))];

        return Stages.Create(entries);
    }

    public void Dispose() => Metrics.Dispose();
}
