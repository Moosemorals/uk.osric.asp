// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary><c>ctx.Stores.Page.Secrets</c> through the real host: one layer's key, and a job's.</summary>
/// <remarks>
/// The unit tests prove the handle and the table; this proves the wiring: a stored page reaches the
/// shape through the real pipeline, a second page does not see the first's secret, and a job's handle
/// is the starting page's, which is what lets a worker sign what it sends.
/// </remarks>
[TestFixture]
public sealed class SecretTests : IntegrationTest {

    private const string KeeperPath = "/secret-keeper";
    private const string ReaderPath = "/secret-reader";
    private const string WorkerPath = "/_work/signer";
    private const string StarterPath = "/secret-starter";
    private const string ViewerPath = "/secret-viewer";

    [Test]
    public async Task APageKeepsASecretAndReadsItBackAsync() {
        const string path = "/secret-own";
        await SavePageAsync(path, """
            <%! using System.Collections.Generic; %>
            <% await ctx.Stores.Page.Secrets.SetAsync("signing", "the-page-key");
               string? found = await ctx.Stores.Page.Secrets.TryGetAsync("signing");
               IReadOnlyList<string> names = await ctx.Stores.Page.Secrets.NamesAsync();
               ctx.Response.Write("found:" + (found ?? "none") + " names:" + string.Join(",", names)); %>
            """);

        using HttpClient client = CreateClient();
        string body = await (await client.GetAsync(path)).Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("found:the-page-key names:signing"), body);
    }

    [Test]
    public async Task AnotherPageCannotReadTheSecretAsync() {
        await SavePageAsync(KeeperPath, """
            <% await ctx.Stores.Page.Secrets.SetAsync("signing", "kept"); %>
            """);
        await SavePageAsync(ReaderPath, """
            <% string? found = await ctx.Stores.Page.Secrets.TryGetAsync("signing");
               ctx.Response.Write("found:" + (found ?? "none")); %>
            """);

        using HttpClient client = CreateClient();
        _ = await client.GetAsync(KeeperPath);
        string body = await (await client.GetAsync(ReaderPath)).Content.ReadAsStringAsync();

        Assert.That(body, Is.EqualTo("found:none"));
    }

    [Test]
    public async Task APageOwnedJobReadsItsStartingPagesSecretAsync() {
        using HttpClient client = await CreateSignedInClientAsync();
        await SaveWorkerAsync(client);
        await SaveStarterAsync(client);
        await SaveViewerAsync(client);

        HttpResponseMessage started = await client.GetAsync(StarterPath);
        string first = await started.Content.ReadAsStringAsync();
        string id = StartedJobId(first);

        string seen = await PollAsync(client, $"{ViewerPath}?id={Uri.EscapeDataString(id)}", "state:Completed");

        Assert.Multiple(() => {
            Assert.That(started.StatusCode, Is.EqualTo(HttpStatusCode.OK), first);
            Assert.That(first, Does.Contain("refused:none"), first);
            Assert.That(seen, Does.Contain("event:the-starting-page-key"), seen);
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
                public sealed class Signer : IWorker {
                    public async Task RunAsync(IWorkerContext context) {
                        string? key = await context.Stores.Page.Secrets.TryGetAsync("signing");
                        context.Publish("done", key ?? "none");
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
               await ctx.Stores.Page.Secrets.SetAsync("signing", "the-starting-page-key");
               PageJobStart start = await ctx.Capabilities.GetRequired<IPageJobs>()
                   .StartAsync("/_work/signer", "hello");
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
               IPageJobs jobs = ctx.Capabilities.GetRequired<IPageJobs>();
               string? id = ctx.Request.Query.TryGetValue("id", out string? given) ? given : null;
               if (id is null) { ctx.Response.Write("no id"); return; }

               PageJobInfo? info = await jobs.GetAsync(id);
               PageJobEvents batch = await jobs.ReadEventsAsync(id, 0);
               ctx.Response.Write("event:" + (batch.Events.Count > 0 ? batch.Events[0].Data : "none"));
               ctx.Response.Write(" state:" + (info?.State.ToString() ?? "gone"));
            %>
            """);

        Assert.That(saved.IsSuccessStatusCode, Is.True,
            $"the viewer was not saved: {(int)saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");
    }
}
