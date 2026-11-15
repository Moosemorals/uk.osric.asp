// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>A page starting a stored worker, and the next request finding the job it started.</summary>
/// <remarks>
/// The capability and the runner are registered by the host, so this is the one place that proves the
/// wiring: a page reaches <c>IPageJobs</c> through the real pipeline, a worker saved by the real
/// editor is compiled, loaded and run, and a second request lists it against the same account.
/// </remarks>
[TestFixture]
public sealed class PageJobTests : IntegrationTest {

    private const string WorkerPath = "/_work/echo";
    private const string StarterPath = "/jobs";
    private const string ListPath = "/jobs-list";

    [Test]
    public async Task APageStartsAWorkerAndTheNextRequestFindsTheJobAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SaveWorkerAsync(client);
        await SaveStarterAsync(client);
        await SaveListAsync(client);

        HttpResponseMessage started = await client.GetAsync(StarterPath);
        string first = await started.Content.ReadAsStringAsync();

        HttpResponseMessage listed = await client.GetAsync(ListPath);
        string second = await listed.Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.OK), first);
            Assert.That(first, Does.Contain("refused:none"), first);
            Assert.That(first, Does.Contain("event:hello"), first);
            Assert.That(first, Does.Contain("state:Completed"), first);
            Assert.That(second, Does.Contain("jobs:1"), second);
        });
    }

    private static async Task SaveWorkerAsync(HttpClient client) {
        HttpResponseMessage saved = await PostEditorAsync(client, WorkerPath, """
            <%!
            using System.Threading.Tasks;

            namespace Site.Work {
                public sealed class Echo : IWorker {
                    public Task RunAsync(IWorkerContext context) {
                        context.Publish("done", context.Input ?? "");
                        return Task.CompletedTask;
                    }
                }
            }
            %>
            """);

        Assert.That(saved.IsSuccessStatusCode, Is.True,
            $"the worker was not saved: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
    }

    private static async Task SaveStarterAsync(HttpClient client) {
        HttpResponseMessage saved = await PostEditorAsync(client, StarterPath, """
            <%! using System.Collections.Generic; %>
            <%
               ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               IPageJobs jobs = ctx.Capabilities.GetRequired<IPageJobs>();
               PageJobStart start = await jobs.StartAsync("/_work/echo", "hello");
               if (!start.Started) {
                   ctx.Write("refused:" + start.Refusal);
                   return;
               }

               string data = "";
               PageJobInfo? info = null;
               for (int attempt = 0; attempt < 20; attempt++) {
                   PageJobEvents batch = await jobs.ReadEventsAsync(start.Id, 0, 1);
                   if (batch.Events.Count > 0) {
                       data = batch.Events[0].Data;
                       break;
                   }
               }

               info = await jobs.GetAsync(start.Id);
               ctx.Write("refused:none event:" + data + " state:" + info?.State);
            %>
            """);

        Assert.That(saved.IsSuccessStatusCode, Is.True,
            $"the starter was not saved: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
    }

    private static async Task SaveListAsync(HttpClient client) {
        HttpResponseMessage saved = await PostEditorAsync(client, ListPath, """
            <%! using System.Collections.Generic; %>
            <%
               ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               IReadOnlyList<PageJobInfo> jobs = await ctx.Capabilities.GetRequired<IPageJobs>().ListAsync();
               ctx.Write("jobs:" + jobs.Count);
            %>
            """);

        Assert.That(saved.IsSuccessStatusCode, Is.True,
            $"the list was not saved: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
    }
}
