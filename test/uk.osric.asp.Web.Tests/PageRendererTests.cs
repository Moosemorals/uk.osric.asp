// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Pages;

/// <summary>Proves what a request gets: cache, lazy compile, budget and abandonment.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// The budget tests assert how late an answer may be, and a page that outruns its budget burns CPU
// until it stops. Running beside another fixture's compiles starves the thread pool and delays the
// deadline continuation, so this fixture has the process to itself.
[NonParallelizable]
public sealed class PageRendererTests {

    // A page that cannot call Thread.Sleep or Task.Delay (the API policy refuses both) burns CPU until
    // a wall-clock deadline, so it is a real slow page that still ends on its own.
    private const string SlowPage =
        "<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(1500); while (System.DateTime.UtcNow < until) { } %>";

    [Test]
    public async Task RenderPropagatesACompilerFaultAsync() {
        FakePageStore storage = new();
        storage["/fault"] = new StoredPage(PageHarness.Key("/fault"), "Hello.");
        using PageHarness pages = new(storage, new FaultingCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pages.RenderAsync("/fault"));
    }

    [Test]
    public async Task RenderOfACompiledPageDoesNotCompileAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        await pages.StoreCompiledAsync("/hello", "Hello.");
        int compiles = pages.Compiler.CompileCount;

        (RenderResult result, string output) = await pages.RenderAsync("/hello");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Hello."));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(compiles));
        Assert.That(storage.CompilationWriteCount, Is.Zero);
    }

    [Test]
    public async Task RenderCompilesAPageThatWasStoredWithoutOneAsync() {
        FakePageStore storage = new();
        storage["/seeded"] = new StoredPage(PageHarness.Key("/seeded"), "Seeded.");
        using PageHarness pages = new(storage);

        (RenderResult result, string output) = await pages.RenderAsync("/seeded");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Seeded."));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
        Assert.That(storage.CompilationWriteCount, Is.EqualTo(1));
        Assert.That(PageHarness.Stored(storage, "/seeded").Compilation, Is.Not.Null);
    }

    [Test]
    public async Task RenderRecompilesWhenTheContentChangedBehindTheCacheAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageCompilation stale = new(ContentFingerprint.Of("Old."), pages.Compiler.RuntimeId, [1, 2, 3], []);
        storage["/edited"] = new StoredPage(PageHarness.Key("/edited"), "New.", stale);

        (RenderResult result, string output) = await pages.RenderAsync("/edited");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("New."));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
        Assert.That(PageHarness.Stored(storage, "/edited").Compilation?.ContentHash, Is.EqualTo(ContentFingerprint.Of("New.")));
    }

    [Test]
    public async Task RenderRecompilesWhenTheBuildChangedAsync() {
        FakePageStore storage = new();
        PageCompilation previousBuild = new(ContentFingerprint.Of("Hello."), "0:an-old-build", [1, 2, 3], []);
        storage["/hello"] = new StoredPage(PageHarness.Key("/hello"), "Hello.", previousBuild);
        using PageHarness pages = new(storage);

        (RenderResult result, string output) = await pages.RenderAsync("/hello");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Hello."));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
        Assert.That(PageHarness.Stored(storage, "/hello").Compilation?.RuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
    }

    [Test]
    public async Task AStoredCompileErrorIsServedWithoutCompilingAgainAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageCompilation failed = new(ContentFingerprint.Of("<% int value = ; %>"), pages.Compiler.RuntimeId, null, [
            new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 16), "CS1525: broken"),
        ]);
        storage["/broken"] = new StoredPage(PageHarness.Key("/broken"), "<% int value = ; %>", failed);

        (RenderResult result, string _) = await pages.RenderAsync("/broken");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.CompileFailed));
        Assert.That(result.Diagnostics, Is.EqualTo(failed.Diagnostics).AsCollection);
        Assert.That(pages.Compiler.CompileCount, Is.Zero);
    }

    [Test]
    public async Task MissingPageIsNotFoundAsync() {
        using PageHarness pages = new(new FakePageStore());

        (RenderResult result, string _) = await pages.RenderAsync("/missing");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.NotFound));
    }

    [Test]
    public async Task RenderKeepsTheNewerPageWhenItChangesWhileCompilingAsync() {
        FakePageStore storage = new();
        storage["/race"] = new StoredPage(PageHarness.Key("/race"), "first");
        storage.ReplaceOnCompilationWrite = (path, _) => new StoredPage(path, "second");
        using PageHarness pages = new(storage);

        (RenderResult result, string output) = await pages.RenderAsync("/race");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("second"));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(2));
        Assert.That(PageHarness.Stored(storage, "/race").Content, Is.EqualTo("second"));
    }

    [Test]
    public async Task TemplateExceptionsPropagateAsync() {
        FakePageStore storage = new();
        storage["/throw"] = new StoredPage(PageHarness.Key("/throw"), "<% throw new System.InvalidOperationException(\"Boom.\"); %>");
        using PageHarness pages = new(storage);

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.RenderAsync("/throw"));

        Assert.That(thrown?.Message, Is.EqualTo("Boom."));
    }

    [Test]
    public async Task APageThatOutrunsTheBudgetIsAbandonedAsync() {
        FakePageStore storage = new();
        RenderOptions options = new() { TimeoutSeconds = 1 };
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, new CompileOptions()), health: health);
        await pages.StoreCompiledAsync("/slow", SlowPage);

        long start = Stopwatch.GetTimestamp();
        (RenderResult result, string _) = await pages.RenderAsync("/slow");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromSeconds(1.4)),
            "the answer must not wait for the page to finish");
        Assert.That(health.Abandoned, Is.EqualTo(1), "an abandoned render is what the health check counts");
    }

    [Test]
    public async Task AnAbandonedPageStopsWritingToTheResponseAsync() {
        FakePageStore storage = new();
        RenderOptions options = new() { TimeoutSeconds = 1 };
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, new CompileOptions()));
        await pages.StoreCompiledAsync("/chatty",
            "before<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(1200); while (System.DateTime.UtcNow < until) { } %>after");

        (RenderResult result, string output) = await pages.RenderAsync("/chatty");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(output, Is.EqualTo("before"));

        // The page is still running when the site answers; give it time to finish and check that its
        // late writes went nowhere.
        await Task.Delay(600);
        Assert.That(output, Is.EqualTo("before"));
    }

    [Test]
    public async Task AnAbandonedPageIsReleasedFromTheHealthCountWhenItFinishesAsync() {
        FakePageStore storage = new();
        RenderOptions options = new() { TimeoutSeconds = 1 };
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, new CompileOptions()), health: health);
        await pages.StoreCompiledAsync("/slow", SlowPage);

        await pages.RenderAsync("/slow");
        Assert.That(health.Abandoned, Is.EqualTo(1));

        Stopwatch wait = Stopwatch.StartNew();
        while (health.Abandoned > 0 && wait.Elapsed < TimeSpan.FromSeconds(3)) {
            await Task.Delay(50);
        }

        Assert.That(health.Abandoned, Is.Zero, "a page that returns late stops counting against the site");
    }

    [Test]
    public async Task APageWhoseCompileOutlivesTheBudgetIsServedByTheNextRequestAsync() {
        FakePageStore storage = new();
        storage["/cold"] = new StoredPage(PageHarness.Key("/cold"), "Hello.");
        RenderOptions options = new() { TimeoutSeconds = 1 };
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, new SlowCompiler(new PageCompiler()), options,
            new PageBudget(options, new CompileOptions()), health);

        (RenderResult first, string firstOutput) = await pages.RenderAsync("/cold");

        Assert.That(first.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(firstOutput, Is.Empty, "a page that has no budget left does not start");
        Assert.That(health.Abandoned, Is.Zero, "nothing ran, so no thread was lost");
        Assert.That(PageHarness.Stored(storage, "/cold").Compilation?.Image, Is.Not.Null,
            "the compile outlived the answer, so its image has to be kept or the page can never be served");

        (RenderResult second, string secondOutput) = await pages.RenderAsync("/cold");

        Assert.That(second.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(secondOutput, Is.EqualTo("Hello."));
    }

    [Test]
    public async Task ARenderThatWaitsPastTheBudgetForASlotIsDroppedAsync() {
        FakePageStore storage = new();
        storage["/hello"] = new StoredPage(PageHarness.Key("/hello"), "Hello.");
        RenderOptions options = new() { TimeoutSeconds = 1, Slots = 1 };
        PageBudget budget = new(options, new CompileOptions());
        using PageHarness pages = new(storage, options: options, budget: budget, health: new PageHealth(options.AbandonedLimit));

        await budget.RenderSlots.WaitAsync();
        try {
            (RenderResult result, string _) = await pages.RenderAsync("/hello");

            Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
            Assert.That(pages.Compiler.CompileCount, Is.Zero);
        } finally {
            budget.RenderSlots.Release();
        }
    }

    [Test]
    public async Task ARenderThatSpendsItsBudgetWaitingForACompileSlotTimesOutAsync() {
        FakePageStore storage = new();
        storage["/hello"] = new StoredPage(PageHarness.Key("/hello"), "Hello.");
        RenderOptions options = new() { TimeoutSeconds = 1 };
        PageBudget budget = new(options, new CompileOptions());
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, options: options, budget: budget, health: health);

        await budget.CompileSlots.WaitAsync();
        try {
            long start = Stopwatch.GetTimestamp();
            (RenderResult result, string output) = await pages.RenderAsync("/hello");

            Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
            Assert.That(output, Is.Empty, "a page that never started wrote nothing");
            Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromSeconds(1.4)),
                "the wait is bounded by the render's budget, not the longer compile budget");
        } finally {
            budget.CompileSlots.Release();
        }

        Assert.That(health.Abandoned, Is.Zero, "no thread was lost, so the site is not counted as unhealthy");
    }

    [Test]
    public async Task ARenderThatSpendsItsBudgetOnTheReadTimesOutWithoutRunningThePageAsync() {
        FakePageStore storage = new() { ReadDelay = TimeSpan.FromMilliseconds(1200) };
        storage["/hello"] = new StoredPage(PageHarness.Key("/hello"), "Hello.");
        RenderOptions options = new() { TimeoutSeconds = 1 };
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, new CompileOptions()), health: health);

        long start = Stopwatch.GetTimestamp();
        (RenderResult result, string output) = await pages.RenderAsync("/hello");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromMilliseconds(1200)),
            "the answer comes when the budget is spent, not when the slow read finishes");
        Assert.That(output, Is.Empty);
        Assert.That(health.Abandoned, Is.Zero, "the page never ran, so no thread was lost");
        Assert.That(pages.Compiler.CompileCount, Is.Zero, "a read that ran out of budget is not followed by a compile");
    }

    [Test]
    public async Task APageThatWritesPastTheBodyCapFailsAsync() {
        FakePageStore storage = new();
        storage["/chatty"] = new StoredPage(PageHarness.Key("/chatty"), "0123456789");
        using PageHarness pages = new(storage, options: new RenderOptions { MaxBodyChars = 5 });

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.RenderAsync("/chatty", maxBodyChars: 5));

        Assert.That(thrown?.Message, Does.Contain("wrote more than 5 characters"));
    }
}
