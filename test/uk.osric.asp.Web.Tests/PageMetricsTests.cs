// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Reflection;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// A MeterListener hears every meter with the page meter's name, including one another test is
// writing to, so this fixture must own the process while it runs.
[NonParallelizable]
public sealed class PageMetricsTests {

    private readonly RecordingCompiler _compiler = new(new TemplateCompiler());
    private readonly PageMetrics _metrics = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    [Test]
    public async Task AServedPageIsMeasuredAsAHitAndAFirstCompileAsASaveAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakeStorage storage = new();
        PageService pages = CreateService(storage);

        await pages.SaveAsync(Key("/hit"), "Hello.");
        await RenderAsync(pages, "/hit");
        await RenderAsync(pages, "/hit");

        Assert.That(_compiler.CompileCount, Is.EqualTo(1), "a saved page must not be compiled again");
        Assert.That(capture.Count("page.compile.duration", "reason", "save"), Is.EqualTo(1));
        Assert.That(capture.Count("page.compile.duration", "reason", "lazy"), Is.Zero);
        Assert.That(capture.Count("page.cache.lookups", "result", "hit"), Is.EqualTo(2));

        IReadOnlyList<MetricCapture.Measurement> renders = capture.For("page.render.duration", "/hit");
        Assert.That(renders, Has.Count.EqualTo(2));
        Assert.That(renders, Has.All.Matches<MetricCapture.Measurement>(
            measurement => measurement is not null && measurement.HasTag("cache", "hit") && measurement.HasTag("outcome", "ok")));
    }

    [Test]
    public async Task AColdPageIsMeasuredAsANeverCompiledMissAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakeStorage storage = new();
        storage["/cold"] = new StoredPage(Key("/cold"), "Hello.");
        PageService pages = CreateService(storage);

        await RenderAsync(pages, "/cold");

        Assert.That(capture.Count("page.cache.staleness", "reason", "never-compiled"), Is.EqualTo(1));
        Assert.That(capture.Count("page.compile.duration", "reason", "lazy"), Is.EqualTo(1));

        IReadOnlyList<MetricCapture.Measurement> renders = capture.For("page.render.duration", "/cold");
        Assert.That(renders, Has.Count.EqualTo(1));
        Assert.That(renders[0].HasTag("cache", "miss"), Is.True);
    }

    [Test]
    public async Task AChangedPageIsAttributedToItsContentAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakeStorage storage = new();
        PageCompilation stale = new(ContentFingerprint.Of("Old."), _compiler.RuntimeId, [1, 2, 3], []);
        storage["/edited"] = new StoredPage(Key("/edited"), "New.", stale);
        PageService pages = CreateService(storage);

        await RenderAsync(pages, "/edited");

        Assert.That(capture.Count("page.cache.staleness", "reason", "content-changed"), Is.EqualTo(1));
    }

    [Test]
    public async Task AChangedBuildIsAttributedToTheRuntimeAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakeStorage storage = new();
        PageCompilation previousBuild = new(ContentFingerprint.Of("Hello."), "0:an-old-build", [1, 2, 3], []);
        storage["/hello"] = new StoredPage(Key("/hello"), "Hello.", previousBuild);
        PageService pages = CreateService(storage);

        await RenderAsync(pages, "/hello");

        Assert.That(capture.Count("page.cache.staleness", "reason", "runtime-changed"), Is.EqualTo(1));
    }

    [Test]
    public async Task ACompileFailureIsItsOwnRenderOutcomeAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakeStorage storage = new();
        storage["/broken"] = new StoredPage(Key("/broken"), "<% int value = ; %>");
        PageService pages = CreateService(storage);

        await RenderAsync(pages, "/broken");

        IReadOnlyList<MetricCapture.Measurement> renders = capture.For("page.render.duration", "/broken");
        Assert.That(renders, Has.Count.EqualTo(1));
        Assert.That(renders[0].HasTag("outcome", "compile-error"), Is.True);
        Assert.That(capture.Count("page.compile.duration", "outcome", "compile-error"), Is.EqualTo(1));
    }

    [Test]
    public async Task AnExceptionIsMeasuredAsAnErrorAndStillPropagatesAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakeStorage storage = new();
        storage["/throw"] = new StoredPage(Key("/throw"), "<% throw new System.InvalidOperationException(); %>");
        PageService pages = CreateService(storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RenderAsync(pages, "/throw"));

        IReadOnlyList<MetricCapture.Measurement> renders = capture.For("page.render.duration", "/throw");
        Assert.That(renders, Has.Count.EqualTo(1));
        Assert.That(renders[0].HasTag("outcome", "error"), Is.True);
        Assert.That(capture.Count("page.handler.invoke.duration", "outcome", "error"), Is.EqualTo(1));
    }

    [Test]
    public async Task TheEmittedImageIsMeasuredAsync() {
        using MetricCapture capture = MetricCapture.Start();
        PageService pages = CreateService(new FakeStorage());

        await pages.SaveAsync(Key("/hello"), "Hello.");

        IReadOnlyList<MetricCapture.Measurement> sizes = capture.For("page.image.size", "/hello");
        Assert.That(sizes, Has.Count.EqualTo(1));
        Assert.That(sizes[0].Value, Is.GreaterThan(0));
    }

    [Test]
    public async Task AnAbandonedRenderIsRecordedAsATimeoutAsync() {
        const string SlowPage =
            "<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(1500); while (System.DateTime.UtcNow < until) { } %>";

        // The capture has to start before the meter so no instrument is missed.
        using MetricCapture capture = MetricCapture.Start();
        using PageMetrics metrics = new();
        FakeStorage storage = new();
        await CreateService(storage).SaveAsync(Key("/slow"), SlowPage);

        RenderOptions options = new() { TimeoutSeconds = 1 };
        PageService pages = new(
            storage,
            _compiler,
            metrics,
            new PageBudget(options, new CompileOptions()),
            new PageHealth(options.AbandonedLimit),
            NullLogger<PageService>.Instance);

        await RenderAsync(pages, "/slow");

        Assert.That(capture.Count("page.render.duration", "outcome", "timeout"), Is.EqualTo(1));
        Assert.That(capture.For("page.render.abandoned", "/slow"), Has.Count.EqualTo(1));
        Assert.That(capture.For("page.render.wait", "/slow"), Has.Count.EqualTo(1));
    }

    private PageService CreateService(FakeStorage storage) {
        return new PageService(
            storage,
            _compiler,
            _metrics,
            new PageBudget(new RenderOptions(), new CompileOptions()),
            new PageHealth(RenderOptions.DefaultAbandonedLimit),
            NullLogger<PageService>.Instance);
    }

    private static async Task RenderAsync(PageService pages, string path) {
        PagePath key = Key(path);
        SimpleContext context = new(NullLoggerFactory.Instance, TestHttp.Request(key.Path), CancellationToken.None, new StringWriter());

        await pages.RenderAsync(key, context, CancellationToken.None);
    }

    private static PagePath Key(string path) {
        return PagePath.Normalize(path);
    }
}
