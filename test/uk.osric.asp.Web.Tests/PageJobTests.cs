// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Work;

/// <summary>What a job does: start a stored worker, talk to it, read it and stop it.</summary>
/// <remarks>
/// The workers here are real: each one is compiled and stored by the saver, then loaded by the runner
/// into its own collectable context, so a test proves the whole path rather than a fake of it.
/// </remarks>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageJobTests {

    private const string WorkerPath = "/_work/echo";

    private static readonly PageUser Owner = new("owner");
    private static readonly PageUser Stranger = new("stranger");

    private const string Recorder = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Recorder : IWorker {
                public async Task RunAsync(IWorkerContext context) {
                    IPageStorage storage = context.Capabilities.GetRequired<IPageStorage>();
                    await storage.SetAsync("input", context.Input ?? "");
                    context.Publish("done", "ok");
                }
            }
        }
        %>
        """;

    private const string Relay = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Relay : IWorker {
                public async Task RunAsync(IWorkerContext context) {
                    IPageStorage storage = context.Capabilities.GetRequired<IPageStorage>();
                    context.Publish("ready", "1");
                    if (await context.ReceiveAsync(30) is { } message) {
                        await storage.SetAsync("message", message);
                        context.Publish("message", message);
                    }
                }
            }
        }
        %>
        """;

    private const string Waiter = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Waiter : IWorker {
                public async Task RunAsync(IWorkerContext context) {
                    string? message = await context.ReceiveAsync();
                    context.Publish("woke", message ?? "none");
                }
            }
        }
        %>
        """;

    private const string Spinner = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Spinner : IWorker {
                public Task RunAsync(IWorkerContext context) {
                    System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(400);
                    while (System.DateTime.UtcNow < until) { }

                    return Task.CompletedTask;
                }
            }
        }
        %>
        """;

    private const string Ticker = """
        <%!
        using System.Globalization;
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Ticker : IWorker {
                public Task RunAsync(IWorkerContext context) {
                    for (int index = 1; index <= 10; index++) {
                        context.Publish("tick", index.ToString(CultureInfo.InvariantCulture));
                    }

                    return Task.CompletedTask;
                }
            }
        }
        %>
        """;

    private const string Faulty = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Faulty : IWorker {
                public Task RunAsync(IWorkerContext context) => throw new System.InvalidOperationException("boom");
            }
        }
        %>
        """;

    private const string RendererCheck = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class RendererCheck : IWorker {
                public Task RunAsync(IWorkerContext context) {
                    context.Publish("renderer",
                        context.Capabilities.Get<IMarkdownRenderer>() is null ? "none" : "present");

                    return Task.CompletedTask;
                }
            }
        }
        %>
        """;

    [Test]
    public async Task AWorkerWritesTheStartingPagesStorageAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreAsync(pages, Recorder);
        LiveStartingPage(pages);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, "hello");
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);
        string? written = await pages.State.GetValueAsync(PageHarness.Key("/starter"), "input");

        Assert.Multiple(() => {
            Assert.That(start.Started, Is.True);
            Assert.That(info.State, Is.EqualTo(PageJobState.Completed));
            Assert.That(written, Is.EqualTo("\"hello\""),
                "the job writes the page's own state, not the worker's");
        });
    }

    [Test]
    public async Task APageSendsAWorkerAMessageAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreAsync(pages, Relay);
        LiveStartingPage(pages);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        PageJobEvents ready = await pages.Jobs.ReadEventsAsync(
            start.Id!, 0, TimeSpan.FromSeconds(5), Owner, CancellationToken.None);
        PageJobSendResult sent = pages.Jobs.Send(PageHarness.Key("/starter"), start.Id!, "later", Owner);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(ready.Events.Select(item => item.Type), Does.Contain("ready"));
            Assert.That(sent.Sent, Is.True, $"refused with {sent.Refusal}");
            Assert.That(info.State, Is.EqualTo(PageJobState.Cancelled).Or.EqualTo(PageJobState.Completed));
            Assert.That(pages.State.GetValueAsync(PageHarness.Key("/starter"), "message").Result,
                Is.EqualTo("\"later\""));
        });
    }

    [Test]
    public async Task AViewerWaitsForTheNextEventAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreAsync(pages, Relay);
        LiveStartingPage(pages);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        PageJobEvents ready = await pages.Jobs.ReadEventsAsync(
            start.Id!, 0, TimeSpan.FromSeconds(5), Owner, CancellationToken.None);
        long seen = ready.Next;
        _ = pages.Jobs.Send(PageHarness.Key("/starter"), start.Id!, "later", Owner);

        PageJobEvents next = await pages.Jobs.ReadEventsAsync(
            start.Id!, seen, TimeSpan.FromSeconds(5), Owner, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(next.Reset, Is.False);
            Assert.That(next.Events.Select(item => item.Data), Does.Contain("later"));
            Assert.That(next.Next, Is.GreaterThan(seen));
        });
    }

    [Test]
    [NonParallelizable]
    public async Task AnIndefiniteWaitOutlivesTheSilenceClockAsync() {
        using PageHarness pages = new(new FakePageStore(), jobOptions: TestOptions.Jobs with {
            SilenceSeconds = 0.1,
            MaxDurationSeconds = 10,
        });
        await StoreAsync(pages, Waiter);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        PageJobSendResult sent = pages.Jobs.Send(PageHarness.Key("/starter"), start.Id!, "hello", Owner);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(sent.Sent, Is.True, "a job waiting for its owner is not a job that went quiet");
            Assert.That(info.State, Is.EqualTo(PageJobState.Completed));
        });
    }

    [Test]
    [NonParallelizable]
    public async Task TheCeilingEndsAnIndefiniteWaitAsync() {
        using PageHarness pages = new(new FakePageStore(), jobOptions: TestOptions.Jobs with {
            SilenceSeconds = 10,
            MaxDurationSeconds = 0.2,
        });
        await StoreAsync(pages, Waiter);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(info.State, Is.EqualTo(PageJobState.Failed));
            Assert.That(info.Failure, Does.Contain("longer than"));
        });
    }

    [Test]
    [NonParallelizable]
    public async Task AJobThatNeitherPublishesNorReceivesIsCutAsync() {
        using PageHarness pages = new(new FakePageStore(), jobOptions: TestOptions.Jobs with {
            SilenceSeconds = 0.1,
            MaxDurationSeconds = 30,
        });
        await StoreAsync(pages, Spinner);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(info.State, Is.EqualTo(PageJobState.Failed));
            Assert.That(info.Failure, Does.Contain("published and received nothing"));
        });
    }

    [Test]
    public async Task ACancelledJobEndsAtItsNextAwaitAsync() {
        using PageHarness pages = new(new FakePageStore(), jobOptions: TestOptions.Jobs with {
            SilenceSeconds = 30,
            MaxDurationSeconds = 60,
        });
        await StoreAsync(pages, Waiter);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        bool cancelled = pages.Jobs.Cancel(PageHarness.Key("/starter"), start.Id!, Owner);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(cancelled, Is.True);
            Assert.That(info.State, Is.EqualTo(PageJobState.Cancelled));
            Assert.That(info.Failure, Is.Null);
        });
    }

    [Test]
    public async Task AWorkerThatThrowsIsAFailedJobAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreAsync(pages, Faulty);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(info.State, Is.EqualTo(PageJobState.Failed));
            Assert.That(info.Failure, Is.EqualTo("boom"));
        });
    }

    [Test]
    public async Task AWorkerThatNoLongerCompilesIsAFailedJobWithItsReasonAsync() {
        using PageHarness pages = new(new FakePageStore());
        await SaveAsync(pages, WorkerPath, """
            <%!
            using System.Threading.Tasks;

            namespace Site.Work {
                public sealed class Broken : IWorker {
                    public Task RunAsync(IWorkerContext context) => Missing(context);
                }
            }
            %>
            """);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(info.State, Is.EqualTo(PageJobState.Failed));
            Assert.That(info.Failure, Does.Contain("CS").Or.Contain("PAGE"));
        });
    }

    [Test]
    public async Task AWorkerIsOfferedNoRendererAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreAsync(pages, RendererCheck);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);
        PageJobEvents events = await pages.Jobs.ReadEventsAsync(
            start.Id!, 0, TimeSpan.Zero, Owner, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(info.State, Is.EqualTo(PageJobState.Completed));
            Assert.That(events.Events.Single().Data, Is.EqualTo("none"),
                "a worker has no document to render into");
        });
    }

    [Test]
    public async Task TheRingDropsTheOldestEventsAndResetsAViewerWhoMissedThemAsync() {
        using PageHarness pages = new(new FakePageStore(), jobOptions: TestOptions.Jobs with { MaxEvents = 3 });
        await StoreAsync(pages, Ticker);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, firstMessage: null);
        _ = await WaitForEndAsync(pages, start.Id!);

        PageJobEvents events = await pages.Jobs.ReadEventsAsync(
            start.Id!, 0, TimeSpan.Zero, Owner, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(events.Reset, Is.True, "the first sequences are gone, so the viewer starts again");
            Assert.That(events.Events, Has.Count.EqualTo(3));
            Assert.That(events.Events[^1].Data, Is.EqualTo("10"));
            Assert.That(events.Next, Is.EqualTo(events.Events[^1].Sequence));
        });
    }

    [Test]
    public async Task AnotherVisitorsJobIsUnknownAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreAsync(pages, Recorder);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, "hello");
        _ = await WaitForEndAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(pages.Jobs.Get(start.Id!, Stranger), Is.Null);
            Assert.That(pages.Jobs.List(Stranger), Is.Empty);
            Assert.That(pages.Jobs.Send(PageHarness.Key("/starter"), start.Id!, "hello", Stranger).Refusal,
                Is.EqualTo(PageJobRefusal.UnknownJob));
            Assert.That(pages.Jobs.Cancel(PageHarness.Key("/starter"), start.Id!, Stranger), Is.False);
            Assert.That(pages.Jobs.Get(start.Id!, Owner), Is.Not.Null, "the owner still reads it");
        });
    }

    [Test]
    public async Task AMessageAfterTheJobEndsIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreAsync(pages, Recorder);
        await pages.StartJobsAsync();

        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, "hello");
        _ = await WaitForEndAsync(pages, start.Id!);

        PageJobSendResult sent = pages.Jobs.Send(PageHarness.Key("/starter"), start.Id!, "again", Owner);

        Assert.That(sent.Refusal, Is.EqualTo(PageJobRefusal.Ended));
    }

    [Test]
    public void AFullPendingQueueIsRefusedAsAValue() {
        PageJobOptions options = TestOptions.Jobs with { MaxPendingMessages = 1 };
        PageJob job = new(
            "job-1", PageHarness.Key(WorkerPath), PageHarness.Key("/starter"), Owner, input: null,
            options, TimeProvider.System);
        using (job) {
            Assert.Multiple(() => {
                Assert.That(job.TrySend("one"), Is.EqualTo(PageJobRefusal.None));
                Assert.That(job.TrySend("two"), Is.EqualTo(PageJobRefusal.TooManyMessages));
            });
        }
    }

    [Test]
    public async Task AKeyThatIsNotAWorkerIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());
        _ = await SaveAsync(pages, "/_lib/helpers", "<%! namespace Site.Helpers { public static class Help { } } %>");
        _ = await SaveAsync(pages, "/ordinary", "<% ctx.Write(\"hi\"); %>");
        _ = await SaveAsync(pages, "/plain.css", "body { }");

        PageJobRefusal missing = (await StartAsync(pages, "/starter", "/missing", null)).Refusal;
        PageJobRefusal ordinary = (await StartAsync(pages, "/starter", "/ordinary", null)).Refusal;
        PageJobRefusal asset = (await StartAsync(pages, "/starter", "/plain.css", null)).Refusal;
        PageJobRefusal library = (await StartAsync(pages, "/starter", "/_lib/helpers", null)).Refusal;
        PageJobRefusal site = (await StartAsync(pages, "/starter", "/login", null)).Refusal;
        PageJobRefusal notAKey = (await StartAsync(pages, "/starter", "/", null)).Refusal;

        Assert.Multiple(() => {
            Assert.That(missing, Is.EqualTo(PageJobRefusal.UnknownWorker));
            Assert.That(ordinary, Is.EqualTo(PageJobRefusal.NotAWorker));
            Assert.That(asset, Is.EqualTo(PageJobRefusal.NotAWorker));
            Assert.That(library, Is.EqualTo(PageJobRefusal.NotAWorker));
            Assert.That(site, Is.EqualTo(PageJobRefusal.OwnedBySite));
            Assert.That(notAKey, Is.EqualTo(PageJobRefusal.InvalidKey));
        });
    }

    [Test]
    public async Task AnAnonymousRenderIsRefusedAStartAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreAsync(pages, Recorder);

        PageJobStart start = await pages.Jobs.StartAsync(
            PageHarness.Key("/starter"), WorkerPath, null, owner: null, CancellationToken.None);

        Assert.That(start.Refusal, Is.EqualTo(PageJobRefusal.Anonymous));
    }

    [Test]
    public async Task AFullQueueIsRefusedAStartAsync() {
        // No runner is started, so the one slot the queue has is never given back.
        using PageHarness pages = new(new FakePageStore(), jobOptions: TestOptions.Jobs with { QueueCapacity = 1 });
        await StoreAsync(pages, Recorder);

        PageJobStart first = await StartAsync(pages, "/starter", WorkerPath, null);
        PageJobStart second = await StartAsync(pages, "/starter", WorkerPath, null);

        Assert.Multiple(() => {
            Assert.That(first.Started, Is.True);
            Assert.That(second.Refusal, Is.EqualTo(PageJobRefusal.QueueFull));
        });
    }

    [Test]
    public async Task ARunningJobTakesNoRenderSlotAsync() {
        using PageHarness pages = new(new FakePageStore(), jobOptions: TestOptions.Jobs with {
            SilenceSeconds = 30,
            MaxDurationSeconds = 60,
        });
        await StoreAsync(pages, Waiter);
        await pages.StartJobsAsync();

        int before = pages.Budget.RenderSlots.CurrentCount;
        PageJobStart start = await StartAsync(pages, "/starter", WorkerPath, null);
        await WaitForRunningAsync(pages, start.Id!);
        int during = pages.Budget.RenderSlots.CurrentCount;

        _ = pages.Jobs.Cancel(PageHarness.Key("/starter"), start.Id!, Owner);
        _ = await WaitForEndAsync(pages, start.Id!);

        Assert.That(during, Is.EqualTo(before), "a job answers nobody, so it takes no render slot");
    }

    private static void LiveStartingPage(PageHarness pages) =>
        pages.State.LivePages.Add(PageHarness.Key("/starter").Key);

    // The harness holds two stores where the host holds one, so a saved template is mirrored into the
    // read side as well: that is the copy a start reads a key's kind from.
    private static async Task<SavedRevision> SaveAsync(PageHarness pages, string path, string source) {
        SavedRevision saved = await pages.Saver.SaveAsync(PageHarness.Key(path), source, CancellationToken.None);
        PageTemplate stored = PageHarness.Stored(pages.Storage, path);
        _ = pages.ReadStore.Save(
            path,
            source,
            templateKind: stored.Kind,
            runtimeId: pages.Compiler.RuntimeId,
            hasImage: stored.Image?.Bytes is not null,
            hasDiagnostics: stored.Image?.Diagnostics is { Count: > 0 });

        return saved;
    }

    private static Task<SavedRevision> StoreAsync(PageHarness pages, string source) =>
        SaveAsync(pages, WorkerPath, source);

    private static Task<PageJobStart> StartAsync(
        PageHarness pages, string page, string worker, string? firstMessage) =>
        pages.Jobs.StartAsync(PageHarness.Key(page), worker, firstMessage, Owner, CancellationToken.None);

    private static async Task<PageJobInfo> WaitForEndAsync(PageHarness pages, string id) {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (true) {
            PageJobInfo? info = pages.Jobs.Get(id, Owner);
            Assert.That(info, Is.Not.Null);
            if (info!.State is not (PageJobState.Queued or PageJobState.Running)) {
                return info;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForRunningAsync(PageHarness pages, string id) {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (true) {
            if (pages.Jobs.Get(id, Owner)?.State is PageJobState.Running) {
                return;
            }

            await Task.Delay(10, timeout.Token);
        }
    }
}
