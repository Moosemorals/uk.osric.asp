// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>A page starting a stored worker, and the next request finding the job it started.</summary>
/// <remarks>
/// The capability and the runner are registered by the host, so this is the one place that proves the
/// wiring: a page reaches <c>IPageJobs</c> through the real pipeline, a worker saved by the real
/// editor is compiled, loaded and run, and a later request finds the job against the same account.
/// <para>
/// The pages here do what a view has to do rather than what is convenient for a test: starting a job
/// is one request and reading it is another, so no page blocks its own render waiting for work — that
/// is the shape <c>/help</c> teaches, and blocking one is how this test first timed out under load.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PageJobTests : IntegrationTest {

    private const string WorkerPath = "/_work/echo";
    private const string StarterPath = "/jobs";
    private const string ViewerPath = "/jobs-view";
    private const string ListPath = "/jobs-list";

    [Test]
    public async Task APageStartsAWorkerAndTheNextRequestFindsTheJobAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SaveWorkerAsync(client);
        await SaveStarterAsync(client);
        await SaveViewerAsync(client);
        await SaveListAsync(client);

        HttpResponseMessage started = await client.GetAsync(StarterPath);
        string first = await started.Content.ReadAsStringAsync();
        string id = StartedJobId(first);

        string seen = await PollAsync(client, $"{ViewerPath}?id={Uri.EscapeDataString(id)}", "state:Completed");
        string listed = await (await client.GetAsync(ListPath)).Content.ReadAsStringAsync();

        Assert.Multiple(() => {
            Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.OK), first);
            Assert.That(first, Does.Contain("refused:none"), first);
            Assert.That(seen, Does.Contain("event:hello"), seen);
            Assert.That(listed, Does.Contain("jobs:1"), listed);
        });
    }

    // The worker runs after the request that started it, so the test waits for it the way a viewer
    // does: by asking again, not by holding one render open.
    private static async Task<string> PollAsync(HttpClient client, string url, string until) {
        string body = string.Empty;
        for (int attempt = 0; attempt < 40; attempt++) {
            body = await (await client.GetAsync(url)).Content.ReadAsStringAsync();
            if (body.Contains(until, StringComparison.Ordinal)) {
                return body;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return body;
    }

    private static string StartedJobId(string body) {
        const string Marker = "job:";
        int start = body.IndexOf(Marker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(-1), body);

        int end = body.IndexOf('<', start);
        string id = end < 0 ? body[(start + Marker.Length)..] : body[(start + Marker.Length)..end];

        return id.Trim();
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
            <%
               ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               PageJobStart start = await ctx.Capabilities.GetRequired<IPageJobs>()
                   .StartAsync("/_work/echo", "hello");
               ctx.Response.Write(start.Started ? "refused:none job:" + start.Id : "refused:" + start.Refusal);
            %>
            """);

        Assert.That(saved.IsSuccessStatusCode, Is.True,
            $"the starter was not saved: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
    }

    private static async Task SaveViewerAsync(HttpClient client) {
        HttpResponseMessage saved = await PostEditorAsync(client, ViewerPath, """
            <%! using System.Collections.Generic; %>
            <%
               ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               IPageJobs jobs = ctx.Capabilities.GetRequired<IPageJobs>();
               string? id = ctx.Request.Query.TryGetValue("id", out string? given) ? given : null;
               if (id is null) { ctx.Response.Write("no id"); return; }

               // The state first, then the events: a job that has already finished has published
               // everything it will, so the read that follows cannot straddle the transition.
               PageJobInfo? info = await jobs.GetAsync(id);
               PageJobEvents batch = await jobs.ReadEventsAsync(id, 0);
               ctx.Response.Write("event:" + (batch.Events.Count > 0 ? batch.Events[0].Data : "none"));
               ctx.Response.Write(" state:" + (info?.State.ToString() ?? "gone"));
            %>
            """);

        Assert.That(saved.IsSuccessStatusCode, Is.True,
            $"the viewer was not saved: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
    }

    private static async Task SaveListAsync(HttpClient client) {
        HttpResponseMessage saved = await PostEditorAsync(client, ListPath, """
            <%! using System.Collections.Generic; %>
            <%
               ctx.Capabilities.GetRequired<IPageLayout>().Layout = null;
               IReadOnlyList<PageJobInfo> jobs = await ctx.Capabilities.GetRequired<IPageJobs>().ListAsync();
               ctx.Response.Write("jobs:" + jobs.Count);
            %>
            """);

        Assert.That(saved.IsSuccessStatusCode, Is.True,
            $"the list was not saved: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
    }
}
