// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Proves a stored page can fetch through the real compiler and renderer.</summary>
[TestFixture]
public sealed class PageFetchRenderTests {

    [Test]
    public async Task APageRendersWhatItFetchedAsync() {
        FakePageFetcher fetcher = new(request => new HttpFetchResponse(
            200,
            new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            "{\"state\":\"up\"}",
            HttpFetchFailure.None,
            false));
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/live", """
            <% IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               HttpFetchResponse response = await fetch.SendAsync(new HttpFetchRequest("https://api.example.com/"));
               using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(response.Body); %>
            <p><%= document.RootElement.GetProperty("state").GetString() %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/live");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("up"));
    }

    [Test]
    public async Task APageCanBranchOnAFailureValueAsync() {
        FakePageFetcher fetcher = new(_ => new HttpFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            HttpFetchFailure.Network,
            false));
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/live", """
            <% IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               HttpFetchResponse response = await fetch.GetAsync("https://api.example.com/"); %>
            <p><%= response.Succeeded ? response.Body : "unavailable" %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/live");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("unavailable"));
    }

    [Test]
    public async Task APageCanReadAndLeaveAnOutboundStreamForRenderCleanupAsync() {
        FakePageFetcher fetcher = new();
        FakeHttpFetchStream stream = new("upstream line");
        fetcher.StreamAnswer = _ => stream;
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/live", """
            <% IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               HttpFetchStream stream = await fetch.StreamAsync(new HttpFetchRequest("https://api.example.com/events"));
               ctx.Response.Write(await stream.ReadLineAsync() ?? "empty"); %>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/live");

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
            Assert.That(output, Is.EqualTo("upstream line"));
            Assert.That(stream.IsClosed, Is.True,
                "an upstream response left open by page code must be closed when its render ends");
            Assert.That(fetcher.StreamRequests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AStreamCostsOneFromTheRenderAllowanceAsync() {
        FakePageFetcher fetcher = new();
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/limited", """
            <% IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               HttpFetchStream first = await fetch.StreamAsync(new HttpFetchRequest("https://api.example.com/first"));
               await first.CloseAsync();
               HttpFetchStream second = await fetch.StreamAsync(new HttpFetchRequest("https://api.example.com/second"));
               ctx.Response.Write(second.Failure.ToString());
               await second.CloseAsync(); %>
            """);

        (RenderResult result, string output) = await pages.RenderAsync(
            "/limited", fetchBudget: OneFetch(pages));

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
            Assert.That(output, Is.EqualTo(nameof(HttpFetchFailure.TooManyRequests)));
            Assert.That(fetcher.StreamRequests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AHeadRenderCannotPostAsync() {
        FakePageFetcher fetcher = new();
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/post", """
            <% IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               HttpFetchResponse response = await fetch.PostAsync("https://api.example.com/", "x"); %>
            <p><%= response.Failure.ToString() %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/post", isHead: true);

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("InvalidRequest"));
        Assert.That(fetcher.Requests, Is.Empty);
    }

    [Test]
    public async Task ARenderMakesNoMoreRequestsThanItsAllowanceAsync() {
        FakePageFetcher fetcher = new(_ => new HttpFetchResponse(
            200,
            new Dictionary<string, string>(),
            "ok",
            HttpFetchFailure.None,
            false));
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/greedy", """
            <%
                IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
                for (int index = 0; index < 4; index++) {
                    HttpFetchResponse response = await fetch.GetAsync("https://api.example.com/");
                    ctx.Response.Write(response.Failure.ToString());
                    ctx.Response.Write(" ");
                }
            %>
            """);

        (RenderResult result, string output) = await pages.RenderAsync(
            "/greedy", fetchBudget: new FetchBudget(2, 0.001, pages.Clock, PageFetchOptions.RateKey));

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("None None TooManyRequests TooManyRequests"));
        Assert.That(fetcher.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AnAbandonedRenderCannotFetchAsync() {
        using PageHarness pages = new(new FakePageStore());
        SimpleContext context = pages.CreateContext(PageHarness.Key("/page"));

        context.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Capabilities.GetRequired<IHttpFetch>().GetAsync("https://api.example.com/"));
    }

    [Test]
    public async Task GetAsyncBuildsAGetRequestAsync() {
        FakePageFetcher fetcher = new();
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        SimpleContext context = pages.CreateContext(PageHarness.Key("/page"));

        await context.Capabilities.GetRequired<IHttpFetch>().GetAsync("https://api.example.com/state");

        HttpFetchRequest request = fetcher.Requests.Single();
        Assert.That(request.Url, Is.EqualTo("https://api.example.com/state"));
        Assert.That(request.Method, Is.EqualTo(HttpFetchMethod.Get));
        Assert.That(request.Body, Is.Null);
        Assert.That(request.Headers, Is.Empty);
    }

    [Test]
    public async Task PostAsyncBuildsAPostRequestWithABodyAsync() {
        FakePageFetcher fetcher = new();
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        SimpleContext context = pages.CreateContext(PageHarness.Key("/page"));

        await context.Capabilities.GetRequired<IHttpFetch>().PostAsync("https://api.example.com/state", "hello");

        HttpFetchRequest request = fetcher.Requests.Single();
        Assert.That(request.Url, Is.EqualTo("https://api.example.com/state"));
        Assert.That(request.Method, Is.EqualTo(HttpFetchMethod.Post));
        Assert.That(request.Body, Is.EqualTo("hello"));
        Assert.That(request.Headers, Is.Empty);
    }

    [Test]
    public async Task APageCanNameAnOperatorCredentialAsync() {
        FakePageFetcher fetcher = new(_ => new HttpFetchResponse(
            200,
            new Dictionary<string, string>(),
            "ok",
            HttpFetchFailure.None,
            false));
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/live", """
            <% IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               HttpFetchResponse response = await fetch.SendAsync(new HttpFetchRequest("https://api.example.com/") {
                   Credential = "assistant",
               }); %>
            <p><%= response.Failure.ToString() %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/live");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("None"));
        Assert.That(fetcher.Requests.Single().Credential, Is.EqualTo("assistant"));
    }

    [Test]
    public async Task TwoLayersSharingABudgetMakeOneRequestTooManyAsync() {
        FakePageFetcher fetcher = new();
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        FetchBudget budget = new(1, 0.001, pages.Clock, PageFetchOptions.RateKey);
        IHttpFetch outer = pages.CreateContext(PageHarness.Key("/page"), budget: budget)
            .Capabilities.GetRequired<IHttpFetch>();
        IHttpFetch inner = pages.CreateContext(PageHarness.Key("/page"), budget: budget)
            .Capabilities.GetRequired<IHttpFetch>();

        await outer.GetAsync("https://api.example.com/");
        HttpFetchResponse second = await inner.GetAsync("https://api.example.com/");

        Assert.That(second.Failure, Is.EqualTo(HttpFetchFailure.TooManyRequests));
        Assert.That(fetcher.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ARequestTheSeamRefusesCostsNothingAsync() {
        FakePageFetcher fetcher = new(_ => new HttpFetchResponse(
            200,
            new Dictionary<string, string>(),
            "ok",
            HttpFetchFailure.None,
            false)) {
            Valid = request => !request.Url.Contains("refused", StringComparison.Ordinal),
        };
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/careful", """
            <%
                IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
                ctx.Response.Write((await fetch.GetAsync("https://api.example.com/refused")).Failure.ToString());
                ctx.Response.Write(" ");
                ctx.Response.Write((await fetch.GetAsync("https://api.example.com/kept")).Failure.ToString());
            %>
            """);

        (RenderResult result, string output) = await pages.RenderAsync(
            "/careful", fetchBudget: OneFetch(pages));

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
            Assert.That(output, Is.EqualTo("InvalidRequest None"),
                "a refused request costs nothing, so the allowance still pays for the request the page meant to make");
            Assert.That(fetcher.Requests, Has.Count.EqualTo(1));
            Assert.That(fetcher.Requests.Single().Url, Does.Contain("kept"));
        });
    }

    [Test]
    public async Task AStreamTheSeamRefusesCostsNothingAsync() {
        FakePageFetcher fetcher = new() {
            Valid = _ => false,
        };
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/careful", """
            <% IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
               ctx.Response.Write((await fetch.StreamAsync(new HttpFetchRequest("https://api.example.com/"))).Failure.ToString()); %>
            """);

        (RenderResult result, string output) = await pages.RenderAsync(
            "/careful", fetchBudget: OneFetch(pages));

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
            Assert.That(output, Is.EqualTo(nameof(HttpFetchFailure.InvalidRequest)));
            Assert.That(fetcher.StreamRequests, Is.Empty);
        });
    }

    [Test]
    public async Task AFailedAttemptStillCostsOneAsync() {
        FakePageFetcher fetcher = new(_ => new HttpFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            HttpFetchFailure.Network,
            false));
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher);
        await pages.StoreCompiledAsync("/careful", """
            <%
                IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
                ctx.Response.Write((await fetch.GetAsync("https://api.example.com/first")).Failure.ToString());
                ctx.Response.Write(" ");
                ctx.Response.Write((await fetch.GetAsync("https://api.example.com/second")).Failure.ToString());
            %>
            """);

        (RenderResult result, string output) = await pages.RenderAsync(
            "/careful", fetchBudget: OneFetch(pages));

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
            Assert.That(output, Is.EqualTo("Network TooManyRequests"),
                "a request that was attempted was paid for, whatever came back");
            Assert.That(fetcher.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ARateRefusalNamesTheRateTheKeyAndTheBurstAsync() {
        FakePageFetcher fetcher = new();
        RecordingLoggerFactory logs = new();
        using PageHarness pages = new(new FakePageStore(), fetch: fetcher, loggers: logs);
        await pages.StoreCompiledAsync("/greedy", """
            <%
                IHttpFetch fetch = ctx.Capabilities.GetRequired<IHttpFetch>();
                await fetch.GetAsync("https://api.example.com/first");
                await fetch.GetAsync("https://api.example.com/second");
            %>
            """);

        (RenderResult result, _) = await pages.RenderAsync(
            "/greedy", fetchBudget: new FetchBudget(1, 2, pages.Clock, PageFetchOptions.RateKey));

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
            Assert.That(
                logs.Entries.Select(entry => entry.Message),
                Has.Some.Contains("2 fetches per second").And.Contains(PageFetchOptions.RateKey).And.Contains("1 at most in one burst"),
                "the refusal names the rate, the key that sets it and the burst");
        });
    }

    private static FetchBudget OneFetch(PageHarness pages) =>
        new(1, 0.001, pages.Clock, PageFetchOptions.RateKey);

    [Test]
    public async Task ARequiredFetchIsRefusedWhenTheRenderOffersNoneAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/needs-fetch", """
            <% _ = ctx.Capabilities.GetRequired<IHttpFetch>(); %>
            """);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.RenderAsync("/needs-fetch", withFetch: false));

        Assert.That(refused?.Message, Does.Contain(nameof(IHttpFetch)));
    }
}
