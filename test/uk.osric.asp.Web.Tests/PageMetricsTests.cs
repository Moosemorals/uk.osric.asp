// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

[TestFixture]
// A MeterListener hears every meter with the page meter's name, including one another test is
// writing to, so this fixture must own the process while it runs.
[NonParallelizable]
public sealed class PageMetricsTests {

    [Test]
    public async Task AServedPageIsMeasuredAsAHitAndAFirstCompileAsASaveAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        await pages.Editor.SaveAsync(Key("/hit"), "Hello.");
        await pages.RenderAsync("/hit");
        await pages.RenderAsync("/hit");

        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1), "a saved page must not be compiled again");
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
        FakePageStore storage = new();
        storage["/cold"] = new StoredPage(Key("/cold"), "Hello.");
        using PageHarness pages = new(storage);

        await pages.RenderAsync("/cold");

        Assert.That(capture.Count("page.cache.staleness", "reason", "never-compiled"), Is.EqualTo(1));
        Assert.That(capture.Count("page.compile.duration", "reason", "lazy"), Is.EqualTo(1));

        IReadOnlyList<MetricCapture.Measurement> renders = capture.For("page.render.duration", "/cold");
        Assert.That(renders, Has.Count.EqualTo(1));
        Assert.That(renders[0].HasTag("cache", "miss"), Is.True);
    }

    [Test]
    public async Task AChangedPageIsAttributedToItsContentAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageCompilation stale = new(ContentFingerprint.Of("Old."), pages.Compiler.RuntimeId, [1, 2, 3], []);
        storage["/edited"] = new StoredPage(Key("/edited"), "New.", stale);

        await pages.RenderAsync("/edited");

        Assert.That(capture.Count("page.cache.staleness", "reason", "content-changed"), Is.EqualTo(1));
    }

    [Test]
    public async Task AChangedBuildIsAttributedToTheRuntimeAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakePageStore storage = new();
        PageCompilation previousBuild = new(ContentFingerprint.Of("Hello."), "0:an-old-build", [1, 2, 3], []);
        storage["/hello"] = new StoredPage(Key("/hello"), "Hello.", previousBuild);
        using PageHarness pages = new(storage);

        await pages.RenderAsync("/hello");

        Assert.That(capture.Count("page.cache.staleness", "reason", "runtime-changed"), Is.EqualTo(1));
    }

    [Test]
    public async Task ACompileFailureIsItsOwnRenderOutcomeAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakePageStore storage = new();
        storage["/broken"] = new StoredPage(Key("/broken"), "<% int value = ; %>");
        using PageHarness pages = new(storage);

        await pages.RenderAsync("/broken");

        IReadOnlyList<MetricCapture.Measurement> renders = capture.For("page.render.duration", "/broken");
        Assert.That(renders, Has.Count.EqualTo(1));
        Assert.That(renders[0].HasTag("outcome", "compile-error"), Is.True);
        Assert.That(capture.Count("page.compile.duration", "outcome", "compile-error"), Is.EqualTo(1));
    }

    [Test]
    public async Task AnExceptionIsMeasuredAsAnErrorAndStillPropagatesAsync() {
        using MetricCapture capture = MetricCapture.Start();
        FakePageStore storage = new();
        storage["/throw"] = new StoredPage(Key("/throw"), "<% throw new System.InvalidOperationException(); %>");
        using PageHarness pages = new(storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pages.RenderAsync("/throw"));

        IReadOnlyList<MetricCapture.Measurement> renders = capture.For("page.render.duration", "/throw");
        Assert.That(renders, Has.Count.EqualTo(1));
        Assert.That(renders[0].HasTag("outcome", "error"), Is.True);
        Assert.That(capture.Count("page.handler.invoke.duration", "outcome", "error"), Is.EqualTo(1));
    }

    [Test]
    public async Task TheEmittedImageIsMeasuredAsync() {
        using MetricCapture capture = MetricCapture.Start();
        using PageHarness pages = new(new FakePageStore());

        await pages.Editor.SaveAsync(Key("/hello"), "Hello.");

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
        FakePageStore storage = new();
        RenderOptions options = new() { TimeoutSeconds = 1 };
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, new CompileOptions()));
        await pages.Editor.SaveAsync(Key("/slow"), SlowPage);

        await pages.RenderAsync("/slow");

        Assert.That(capture.Count("page.render.duration", "outcome", "timeout"), Is.EqualTo(1));
        Assert.That(capture.For("page.render.abandoned", "/slow"), Has.Count.EqualTo(1));
        Assert.That(capture.Count("page.render.wait"), Is.EqualTo(1), "the wait is site-wide, not per page");
    }

    [Test]
    public async Task AMissIsRecordedUnderAFixedPathAsync() {
        using MetricCapture capture = MetricCapture.Start();
        using PageHarness pages = new(new FakePageStore());

        await pages.RenderAsync("/a-scan-probably-made-this-up");

        Assert.That(capture.Count("page.render.duration", "outcome", PageMetrics.RenderNotFound), Is.EqualTo(1));
        Assert.That(capture.Count("page.render.duration", "page.path", PageMetrics.NotFoundPath), Is.EqualTo(1));
        Assert.That(capture.For("page.render.duration", "/a-scan-probably-made-this-up"), Is.Empty);
    }

    private static PagePath Key(string path) {
        return PagePath.Normalize(path);
    }
}
