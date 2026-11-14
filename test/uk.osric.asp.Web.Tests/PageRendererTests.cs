// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
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

    // The behaviour under test is the deadline, not its length, so the budget is far shorter than a
    // visitor's: a quarter of a second instead of five.
    private const double BudgetSeconds = 0.25;

    // A page that cannot call Thread.Sleep or Task.Delay (the API policy refuses both) burns CPU until
    // a wall-clock deadline, so it is a real slow page that still ends on its own. It outlasts the
    // budget by a wide margin, so the answer cannot be waiting for it, and it stops soon enough that
    // the rest of this fixture does not run beside a core that is still spinning.
    private const string SlowPage =
        "<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(600); while (System.DateTime.UtcNow < until) { } %>";

    // The deadline continuation has to be scheduled after a 250 ms budget, so the grace is 400 ms;
    // what it absorbs is a busy machine rather than a slow page.
    private static readonly TimeSpan BudgetWithGrace = TimeSpan.FromMilliseconds(650);

    [Test]
    public async Task RenderPropagatesACompilerFaultAsync() {
        FakePageStore storage = new();
        storage["/fault"] = new PageTemplate(PageHarness.Key("/fault"), "<% ctx.Write(\"Hello.\"); %>");
        using PageHarness pages = new(storage, new FaultingCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(() => pages.RenderAsync("/fault"));
    }

    [Test]
    public async Task RenderOfACompiledPageDoesNotCompileAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        await pages.StoreCompiledAsync("/hello", "<% ctx.Write(\"Hello.\"); %>");
        int compiles = pages.Compiler.CompileCount;

        (RenderResult result, string output) = await pages.RenderAsync("/hello");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Hello."));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(compiles));
        Assert.That(storage.ImageWriteCount, Is.Zero);
    }

    [Test]
    public async Task RenderCompilesAPageThatWasStoredWithoutOneAsync() {
        FakePageStore storage = new();
        storage["/seeded"] = new PageTemplate(PageHarness.Key("/seeded"), "<% ctx.Write(\"Seeded.\"); %>");
        using PageHarness pages = new(storage);

        (RenderResult result, string output) = await pages.RenderAsync("/seeded");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Seeded."));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
        Assert.That(storage.ImageWriteCount, Is.EqualTo(1));
        Assert.That(PageHarness.Stored(storage, "/seeded").Image, Is.Not.Null);
    }

    [Test]
    public async Task RenderRecompilesWhenTheContentChangedBehindTheCacheAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageImage stale = new(TemplateFingerprint.Of("<% ctx.Write(\"Old.\"); %>"), pages.Compiler.RuntimeId, [1, 2, 3], null, []);
        storage["/edited"] = new PageTemplate(PageHarness.Key("/edited"), "<% ctx.Write(\"New.\"); %>", stale);

        (RenderResult result, string output) = await pages.RenderAsync("/edited");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("New."));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
        Assert.That(PageHarness.Stored(storage, "/edited").Image?.TemplateHash, Is.EqualTo(TemplateFingerprint.Of("<% ctx.Write(\"New.\"); %>")));
    }

    [Test]
    public async Task RenderRecompilesWhenTheBuildChangedAsync() {
        FakePageStore storage = new();
        PageImage previousBuild = new(TemplateFingerprint.Of("<% ctx.Write(\"Hello.\"); %>"), "0:an-old-build", [1, 2, 3], null, []);
        storage["/hello"] = new PageTemplate(PageHarness.Key("/hello"), "<% ctx.Write(\"Hello.\"); %>", previousBuild);
        using PageHarness pages = new(storage);

        (RenderResult result, string output) = await pages.RenderAsync("/hello");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("Hello."));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
        Assert.That(PageHarness.Stored(storage, "/hello").Image?.RuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
    }

    [Test]
    public async Task AStoredCompileErrorIsServedWithoutCompilingAgainAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageImage failed = new(TemplateFingerprint.Of("<% int value = ; %>"), pages.Compiler.RuntimeId, null, null, [
            new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 16), "CS1525: broken"),
        ]);
        storage["/broken"] = new PageTemplate(PageHarness.Key("/broken"), "<% int value = ; %>", failed);

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
        storage["/race"] = new PageTemplate(PageHarness.Key("/race"), "<% ctx.Write(\"first\"); %>");
        storage.ReplaceOnImageWrite = (path, _) => new PageTemplate(path, "<% ctx.Write(\"second\"); %>");
        using PageHarness pages = new(storage);

        (RenderResult result, string output) = await pages.RenderAsync("/race");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("second"));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(2));
        Assert.That(PageHarness.Stored(storage, "/race").Template, Is.EqualTo("<% ctx.Write(\"second\"); %>"));
    }

    [Test]
    public async Task RenderDoesNotAttachACompileToANewRevisionWithTheSameSourceAsync() {
        FakePageStore storage = new();
        storage["/same"] = new PageTemplate(PageHarness.Key("/same"), "<% ctx.Write(\"same source\"); %>");
        storage.ReplaceOnImageWrite = (path, _) => new PageTemplate(path, "<% ctx.Write(\"same source\"); %>");
        using PageHarness pages = new(storage);

        (RenderResult result, string output) = await pages.RenderAsync("/same");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Is.EqualTo("same source"));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(2));
        Assert.That(PageHarness.Stored(storage, "/same").Image, Is.Not.Null);
    }

    [Test]
    public async Task RenderDoesNotExecuteARevisionDeletedWhileItWasCompilingAsync() {
        FakePageStore storage = new();
        storage["/deleted-during-compile"] = new PageTemplate(PageHarness.Key("/deleted-during-compile"), "<% ctx.Write(\"deleted source\"); %>");
        storage.DeleteOnImageWrite = true;
        using PageHarness pages = new(storage);

        (RenderResult result, string output) = await pages.RenderAsync("/deleted-during-compile");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.NotFound));
        Assert.That(output, Is.Empty);
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
    }

    [Test]
    public async Task TemplateExceptionsPropagateAsync() {
        FakePageStore storage = new();
        storage["/throw"] = new PageTemplate(PageHarness.Key("/throw"), "<% throw new System.InvalidOperationException(\"Boom.\"); %>");
        using PageHarness pages = new(storage);

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.RenderAsync("/throw"));

        Assert.That(thrown?.Message, Is.EqualTo("Boom."));
    }

    [Test]
    public async Task APageThatThrowsAfterAnAwaitIsAPageFaultNotATimeoutAsync() {
        FakePageStore storage = new();
        storage["/throw"] = new PageTemplate(
            PageHarness.Key("/throw"),
            "<% await System.Threading.Tasks.Task.CompletedTask; throw new System.InvalidOperationException(\"Boom after await.\"); %>");
        using PageHarness pages = new(storage);

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.RenderAsync("/throw"));

        Assert.That(thrown?.Message, Is.EqualTo("Boom after await."));
    }

    [Test]
    public async Task APageThatOutrunsTheBudgetIsAbandonedAsync() {
        FakePageStore storage = new();
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = BudgetSeconds };
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, TestOptions.Compile), health: health);
        await pages.StoreCompiledAsync("/slow", SlowPage);

        long start = Stopwatch.GetTimestamp();
        (RenderResult result, string _) = await pages.RenderAsync("/slow");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(BudgetWithGrace),
            "the answer must not wait for the page to finish");
        Assert.That(health.Abandoned, Is.EqualTo(1), "an abandoned render is what the health check counts");
    }

    [Test]
    public async Task AnAbandonedPageStopsWritingToTheResponseAsync() {
        FakePageStore storage = new();
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = BudgetSeconds };
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, TestOptions.Compile));
        await pages.StoreCompiledAsync("/chatty",
            "before<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(600); while (System.DateTime.UtcNow < until) { } %>after");

        (RenderResult result, string output) = await pages.RenderAsync("/chatty");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(output, Is.EqualTo("before"));

        // The page is still running when the site answers; give it time to finish and check that its
        // late writes went nowhere.
        await Task.Delay(500);
        Assert.That(output, Is.EqualTo("before"));
    }

    [Test]
    public async Task AnAbandonedPageWaitingOnStorageIsCancelledAndReleasedAsync() {
        FakePageStore storage = new();
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = BudgetSeconds };
        PageHealth health = new(options.AbandonedLimit);
        FakePageStateStore state = new() { ReadDelay = TimeSpan.FromSeconds(30) };
        state.LivePages.Add("/waiting");
        RecordingLoggerFactory logs = new();
        using PageHarness pages = new(
            storage,
            options: options,
            budget: new PageBudget(options, TestOptions.Compile),
            health: health,
            state: state,
            rendererLogger: logs.CreateLogger<PageRenderer>());
        await pages.StoreCompiledAsync("/waiting",
            "<% ctx.Write((await ctx.Capabilities.GetRequired<IPageStorage>().TryGetValueAsync<int>(\"visits\")).Found.ToString()); %>");

        long start = Stopwatch.GetTimestamp();
        (RenderResult result, string _) = await pages.RenderAsync("/waiting");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(BudgetWithGrace),
            "the answer must not wait for the page to stop waiting");

        // A page suspended in an await unwinds as soon as the deadline fires, so its render is
        // released almost immediately rather than when a network call gives up.
        Stopwatch wait = Stopwatch.StartNew();
        while (health.Abandoned > 0 && wait.Elapsed < TimeSpan.FromSeconds(2)) {
            await Task.Delay(20);
        }

        Assert.That(health.Abandoned, Is.Zero, "a cancelled render releases its health count promptly");
        Assert.That(logs.Entries.Where(entry => entry.Level == LogLevel.Error), Is.Empty,
            "the page was cancelled, not faulted");
    }

    [Test]
    public async Task AnAbandonedPageIsReleasedFromTheHealthCountWhenItFinishesAsync() {
        FakePageStore storage = new();
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = BudgetSeconds };
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, TestOptions.Compile), health: health);
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
        storage["/cold"] = new PageTemplate(PageHarness.Key("/cold"), "<% ctx.Write(\"Hello.\"); %>");
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = BudgetSeconds };
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, new SlowCompiler(new PageCompiler()), options,
            new PageBudget(options, TestOptions.Compile), health);

        (RenderResult first, string firstOutput) = await pages.RenderAsync("/cold");

        Assert.That(first.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(firstOutput, Is.Empty, "a page that has no budget left does not start");
        Assert.That(health.Abandoned, Is.Zero, "nothing ran, so no render was abandoned");
        Assert.That(PageHarness.Stored(storage, "/cold").Image?.Bytes, Is.Not.Null,
            "the compile outlived the answer, so its image has to be kept or the page can never be served");

        (RenderResult second, string secondOutput) = await pages.RenderAsync("/cold");

        Assert.That(second.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(secondOutput, Is.EqualTo("Hello."));
    }

    [Test]
    public async Task ARenderThatWaitsPastTheBudgetForASlotIsDroppedAsync() {
        FakePageStore storage = new();
        storage["/hello"] = new PageTemplate(PageHarness.Key("/hello"), "<% ctx.Write(\"Hello.\"); %>");
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = BudgetSeconds, Slots = 1 };
        PageBudget budget = new(options, TestOptions.Compile);
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
        storage["/hello"] = new PageTemplate(PageHarness.Key("/hello"), "<% ctx.Write(\"Hello.\"); %>");
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = BudgetSeconds };
        PageBudget budget = new(options, TestOptions.Compile);
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, options: options, budget: budget, health: health);

        await budget.CompileSlots.WaitAsync();
        try {
            long start = Stopwatch.GetTimestamp();
            (RenderResult result, string output) = await pages.RenderAsync("/hello");

            Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
            Assert.That(output, Is.Empty, "a page that never started wrote nothing");
            Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(BudgetWithGrace),
                "the wait is bounded by the render's budget, not the longer compile budget");
        } finally {
            budget.CompileSlots.Release();
        }

        Assert.That(health.Abandoned, Is.Zero, "no render was abandoned, so the site is not counted as unhealthy");
    }

    [Test]
    public async Task ARenderThatSpendsItsBudgetOnTheReadTimesOutWithoutRunningThePageAsync() {
        FakePageStore storage = new() { ReadDelay = TimeSpan.FromMilliseconds(600) };
        storage["/hello"] = new PageTemplate(PageHarness.Key("/hello"), "<% ctx.Write(\"Hello.\"); %>");
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = BudgetSeconds };
        PageHealth health = new(options.AbandonedLimit);
        using PageHarness pages = new(storage, options: options,
            budget: new PageBudget(options, TestOptions.Compile), health: health);

        long start = Stopwatch.GetTimestamp();
        (RenderResult result, string output) = await pages.RenderAsync("/hello");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Timeout));
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromMilliseconds(500)),
            "the answer comes when the budget is spent, not when the slow read finishes");
        Assert.That(output, Is.Empty);
        Assert.That(health.Abandoned, Is.Zero, "the page never ran, so no render was abandoned");
        Assert.That(pages.Compiler.CompileCount, Is.Zero, "a read that ran out of budget is not followed by a compile");
    }

    [Test]
    public async Task APageThatWritesPastTheBodyCapFailsAsync() {
        FakePageStore storage = new();
        storage["/chatty"] = new PageTemplate(PageHarness.Key("/chatty"), "0123456789");
        using PageHarness pages = new(storage, options: TestOptions.Render with { MaxBodyChars = 5 });

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.RenderAsync("/chatty", maxBodyChars: 5));

        Assert.That(thrown?.Message, Does.Contain("wrote more than 5 characters"));
    }
}
