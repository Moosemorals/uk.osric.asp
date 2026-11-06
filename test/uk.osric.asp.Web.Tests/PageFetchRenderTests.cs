// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Proves a stored page can fetch through the real compiler and renderer.</summary>
[TestFixture]
public sealed class PageFetchRenderTests {

    [Test]
    public async Task APageRendersWhatItFetchedAsync() {
        FakePageFetcher fetcher = new(request => new PageFetchResponse(
            200,
            new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            "{\"state\":\"up\"}",
            PageFetchFailure.None,
            false));
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/live", """
            <% PageFetchResponse response = await ctx.FetchAsync(new PageFetchRequest("https://api.example.com/"));
               using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(response.Body); %>
            <p><%= document.RootElement.GetProperty("state").GetString() %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/live");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("up"));
    }

    [Test]
    public async Task APageCanBranchOnAFailureValueAsync() {
        FakePageFetcher fetcher = new(_ => new PageFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            PageFetchFailure.NotAllowed,
            false));
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/live", """
            <% PageFetchResponse response = await ctx.FetchAsync(new PageFetchRequest("https://api.example.com/")); %>
            <p><%= response.Succeeded ? response.Body : "unavailable" %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/live");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("unavailable"));
    }

    [Test]
    public async Task AHeadRenderCannotPostAsync() {
        FakePageFetcher fetcher = new();
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/post", """
            <% PageFetchResponse response = await ctx.FetchAsync(
                   new PageFetchRequest("https://api.example.com/") { Method = PageFetchMethod.Post, Body = "x" }); %>
            <p><%= response.Failure.ToString() %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/post", isHead: true);

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("InvalidRequest"));
        Assert.That(fetcher.Requests, Is.Empty);
    }

    [Test]
    public async Task ARenderMakesNoMoreRequestsThanItsCapAsync() {
        FakePageFetcher fetcher = new(_ => new PageFetchResponse(
            200,
            new Dictionary<string, string>(),
            "ok",
            PageFetchFailure.None,
            false));
        PageFetchOptions options = new() { MaxRequestsPerRender = 2 };
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher, fetchOptions: options);
        await pages.StoreCompiledAsync("/greedy", """
            <%
                for (int index = 0; index < 4; index++) {
                    PageFetchResponse response = await ctx.FetchAsync(new PageFetchRequest("https://api.example.com/"));
                    ctx.Write(response.Failure.ToString());
                    ctx.Write(" ");
                }
            %>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/greedy");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("None None TooManyRequests TooManyRequests"));
        Assert.That(fetcher.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AnAbandonedRenderCannotFetchAsync() {
        PageFetchOptions options = new();
        SimpleContext context = new(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            TestHttp.Request("/page"),
            CancellationToken.None,
            new StringWriter(),
            new PageStorageScope(
                uk.osric.asp.Core.Pages.PagePath.Normalize("/page"),
                new FakePageStateStore(),
                new PageStorageOptions(),
                writable: true),
            new FakePageFetcher(),
            options,
            new PageMetrics());

        context.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.FetchAsync(new PageFetchRequest("https://api.example.com/")));
    }
}
