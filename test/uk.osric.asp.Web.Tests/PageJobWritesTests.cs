// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

/// <summary>What a worker's writer does while its borrowed credential is live, and after it dies.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageJobWritesTests {

    private const string WorkerPath = "/_work/writer";
    private const string ChildPath = "/_work/child";

    private static readonly PageUser Owner = new("owner");

    private const string Writer = """
        <%!
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Writer : IWorker {
                public async Task RunAsync(IWorkerContext context) {
                    IPageWrites writes = context.Capabilities.GetRequired<IPageWrites>();

                    PageEditResult first = await writes.SaveAsync("/one", "first", null);
                    context.Publish("first", first.Outcome.ToString());

                    if (await context.ReceiveAsync(30) is null) {
                        return;
                    }

                    PageEditResult second = await writes.SaveAsync("/two", "second", null);
                    context.Publish("second", second.Outcome.ToString());

                    await using IPageTransaction batch = writes.BeginTransaction();
                    _ = await writes.SaveAsync("/three", "three", null);
                    _ = await writes.SaveAsync("/four", "four", null);
                    IReadOnlyList<PageWriteResult> report = await batch.CommitAsync();
                    foreach (PageWriteResult row in report) {
                        context.Publish("row", row.Outcome.ToString());
                    }

                    PageJobStart child = await context.Capabilities.GetRequired<IPageJobs>()
                        .StartAsync("/_work/child", null);
                    context.Publish("child", child.Refusal.ToString());
                }
            }
        }
        %>
        """;

    private const string Child = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Child : IWorker {
                public Task RunAsync(IWorkerContext context) {
                    context.Publish("child-ran", "yes");

                    return Task.CompletedTask;
                }
            }
        }
        %>
        """;

    [Test]
    public async Task AJobWritesTheStoreWhileItsCredentialIsLiveAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreWorkerAsync(pages, WorkerPath, Writer);
        await StoreWorkerAsync(pages, ChildPath, Child);
        await pages.StartJobsAsync();
        PageJobAuthority authority = pages.Authority(Owner);

        PageJobStart start = await StartAsync(pages, authority);
        Assert.That(start.Started, Is.True, start.Refusal.ToString());
        _ = await WaitForEventAsync(pages, start.Id!, "first");
        _ = pages.Jobs.Send(PageHarness.Key("/starter"), start.Id!, "go", Owner);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);
        PageJobEvents all = await EventsAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(info.State, Is.EqualTo(PageJobState.Completed));
            Assert.That(Data(all, "first"), Is.EqualTo(["Saved"]));
            Assert.That(Data(all, "second"), Is.EqualTo(["Saved"]));
            Assert.That(Data(all, "child"), Is.EqualTo(["None"]));
            Assert.That(PageHarness.Stored(pages.Storage, "/two").Template, Is.EqualTo("second"),
                "a worker writes any key the store allows, not just the page that started it");
        });
    }

    [Test]
    public async Task ARevokedCredentialRefusesEveryWriteAndTheJobKeepsGoingAsync() {
        using PageHarness pages = new(new FakePageStore());
        await StoreWorkerAsync(pages, WorkerPath, Writer);
        await StoreWorkerAsync(pages, ChildPath, Child);
        await pages.StartJobsAsync();
        PageJobAuthority authority = pages.Authority(Owner);

        PageJobStart start = await StartAsync(pages, authority);
        Assert.That(start.Started, Is.True, start.Refusal.ToString());
        _ = await WaitForEventAsync(pages, start.Id!, "first");
        pages.Revoke(authority);
        _ = pages.Jobs.Send(PageHarness.Key("/starter"), start.Id!, "go", Owner);
        PageJobInfo info = await WaitForEndAsync(pages, start.Id!);
        PageJobEvents all = await EventsAsync(pages, start.Id!);

        Assert.Multiple(() => {
            Assert.That(Data(all, "first"), Is.EqualTo(["Saved"]), "the first write landed while it was live");
            Assert.That(Data(all, "second"), Is.EqualTo(["Revoked"]));
            Assert.That(Data(all, "row"), Is.All.EqualTo("Revoked"), "every staged operation is refused");
            Assert.That(Data(all, "child"), Is.EqualTo(["Revoked"]));
            Assert.That(info.State, Is.EqualTo(PageJobState.Completed),
                "a refusal is a value, so the worker finishes rather than failing");
            Assert.That(pages.Storage.GetPageAsync(PageHarness.Key("/two")).Result, Is.Null);
            Assert.That(pages.Storage.GetPageAsync(PageHarness.Key("/three")).Result, Is.Null);
        });
    }

    private static Task<PageJobStart> StartAsync(PageHarness pages, PageJobAuthority authority) =>
        pages.Jobs.StartAsync(
            PageHarness.Key("/starter"), WorkerPath, null, authority, CancellationToken.None);

    private static async Task StoreWorkerAsync(PageHarness pages, string path, string source) {
        _ = await pages.Saver.SaveAsync(PageHarness.Key(path), source, CancellationToken.None);
        PageTemplate stored = PageHarness.Stored(pages.Storage, path);
        Assert.That(stored.Kind, Is.EqualTo(Core.Pages.PageKind.Worker),
            string.Join("; ", (stored.Image?.Diagnostics ?? []).Select(item => item.Message)));
        _ = pages.ReadStore.Save(
            path,
            source,
            templateKind: stored.Kind,
            runtimeId: pages.Compiler.RuntimeId,
            hasImage: stored.Image?.Bytes is not null,
            hasDiagnostics: stored.Image?.Diagnostics is { Count: > 0 });
    }

    private static async Task<PageJobEvents> WaitForEventAsync(PageHarness pages, string id, string type) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline) {
            PageJobEvents events = await EventsAsync(pages, id);
            if (Data(events, type).Count > 0) {
                return events;
            }

            PageJobInfo? info = pages.Jobs.Get(id, Owner);
            if (info?.State is PageJobState.Failed or PageJobState.Cancelled) {
                Assert.Fail($"the job ended as {info.State} before publishing {type}: {info.Failure}");
            }

            await Task.Delay(10);
        }

        PageJobInfo? last = pages.Jobs.Get(id, Owner);
        PageJobEvents seen = await EventsAsync(pages, id);
        Assert.Fail(
            $"timed out waiting for {type}; state {(last is null ? "unknown" : last.State.ToString())}, failure {last?.Failure}, "
            + $"events {string.Join(", ", seen.Events.Select(item => item.Type + "=" + item.Data))}");
        return seen;
    }

    private static Task<PageJobEvents> EventsAsync(PageHarness pages, string id) =>
        pages.Jobs.ReadEventsAsync(id, 0, TimeSpan.FromSeconds(1), Owner, CancellationToken.None);

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

    private static IReadOnlyList<string> Data(PageJobEvents events, string type) =>
        [.. events.Events.Where(item => string.Equals(item.Type, type, StringComparison.Ordinal))
            .Select(item => item.Data)];
}
