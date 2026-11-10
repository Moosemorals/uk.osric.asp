// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageFetcherTests {

    [Test]
    public async Task AUrlIsFetchedAsync() {
        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/status"),
            Respond(HttpStatusCode.OK, "{\"state\":\"up\"}"));

        Assert.That(response.Succeeded, Is.True);
        Assert.That(response.Status, Is.EqualTo(200));
        Assert.That(response.Body, Is.EqualTo("{\"state\":\"up\"}"));
        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.None));
        Assert.That(response.Truncated, Is.False);
    }

    [Test]
    public async Task ThePageUserAgentIsSentAsync() {
        StubHttpMessageHandler handler = new(request => {
            Assert.That(request.Headers.UserAgent.ToString(), Is.EqualTo("moosemorals.com (page)"));

            return Respond(HttpStatusCode.OK, "ok")(request);
        });

        HttpFetchResponse response = await FetchAsync(Request("https://api.example.com/"), handler);

        Assert.That(response.Succeeded, Is.True);
    }

    [Test]
    public async Task APageCannotReplaceTheUserAgentAsync() {
        StubHttpMessageHandler handler = new(request => {
            Assert.That(request.Headers.UserAgent.ToString(), Is.EqualTo("moosemorals.com (page)"));

            return Respond(HttpStatusCode.OK, "ok")(request);
        });

        await FetchAsync(
            new HttpFetchRequest("https://api.example.com/") {
                Headers = new Dictionary<string, string> { ["User-Agent"] = "somebody-else" },
            },
            handler);
    }

    [Test]
    public async Task AnyHttpHostIsFetchedAsync() {
        StubHttpMessageHandler handler = new(Respond(HttpStatusCode.OK, "yes"));

        HttpFetchResponse response = await FetchAsync(Request("https://elsewhere.example/"), handler);

        Assert.That(response.Succeeded, Is.True);
        Assert.That(response.Body, Is.EqualTo("yes"));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [TestCase("ftp://api.example.com/")]
    [TestCase("mailto:someone@example.com")]
    [TestCase("https://user:pass@api.example.com/")]
    [TestCase("https://api.example.com/#fragment")]
    [TestCase("not a url")]
    public async Task AnUnacceptableUrlIsInvalidWithoutASocketAsync(string url) {
        StubHttpMessageHandler handler = new(Respond(HttpStatusCode.OK, "no"));

        HttpFetchResponse response = await FetchAsync(Request(url), handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.InvalidRequest));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task APostCarriesItsBodyAndHeadersAsync() {
        StubHttpMessageHandler handler = new(request => {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Content?.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(request.Headers.Contains("X-Trace"), Is.True);

            return Respond(HttpStatusCode.Created, "made")(request);
        });

        HttpFetchResponse response = await FetchAsync(
            new HttpFetchRequest("https://api.example.com/items") {
                Method = HttpFetchMethod.Post,
                Body = "{\"name\":\"x\"}",
                Headers = new Dictionary<string, string> {
                    ["Content-Type"] = "application/json",
                    ["X-Trace"] = "1",
                },
            },
            handler);

        Assert.That(response.Succeeded, Is.True);
        Assert.That(response.Status, Is.EqualTo(201));
    }

    [Test]
    public async Task APostWithoutAContentTypeSendsPlainTextAsync() {
        StubHttpMessageHandler handler = new(request => {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Content?.Headers.ContentType?.MediaType, Is.EqualTo("text/plain"));

            return Respond(HttpStatusCode.OK, "made")(request);
        });

        HttpFetchResponse response = await FetchAsync(
            new HttpFetchRequest("https://api.example.com/items") {
                Method = HttpFetchMethod.Post,
                Body = "hello",
            },
            handler);

        Assert.That(response.Succeeded, Is.True);
    }

    [Test]
    public async Task AGetWithABodyIsInvalidAsync() {
        StubHttpMessageHandler handler = new(Respond(HttpStatusCode.OK, "no"));

        HttpFetchResponse response = await FetchAsync(
            new HttpFetchRequest("https://api.example.com/") { Body = "{}" },
            handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.InvalidRequest));
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase("Cookie")]
    [TestCase("Host")]
    [TestCase("Transfer-Encoding")]
    public async Task ADisallowedRequestHeaderIsInvalidAsync(string name) {
        StubHttpMessageHandler handler = new(Respond(HttpStatusCode.OK, "no"));

        HttpFetchResponse response = await FetchAsync(
            new HttpFetchRequest("https://api.example.com/") {
                Headers = new Dictionary<string, string> { [name] = "value" },
            },
            handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.InvalidRequest));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task AHeaderWithALineBreakIsInvalidAsync() {
        StubHttpMessageHandler handler = new(Respond(HttpStatusCode.OK, "no"));

        HttpFetchResponse response = await FetchAsync(
            new HttpFetchRequest("https://api.example.com/") {
                Headers = new Dictionary<string, string> { ["X-Trace"] = "one\r\ntwo" },
            },
            handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.InvalidRequest));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task ResponseHeadersAreFilteredAsync() {
        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/"),
            _ => {
                HttpResponseMessage message = new(HttpStatusCode.OK) { Content = Content("body") };
                message.Headers.TryAddWithoutValidation("Set-Cookie", "secret=1");
                message.Headers.TryAddWithoutValidation("X-Trace", "abc");
                message.Content.Headers.TryAddWithoutValidation("Content-Language", "en");

                return message;
            });

        Assert.That(response.Headers.Keys, Does.Contain("X-Trace").And.Contain("Content-Language"));
        Assert.That(response.Headers.Keys, Does.Not.Contain("Set-Cookie"));
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task A4xxOr5xxIsAnHttpErrorWithTheRealStatusAsync(HttpStatusCode status) {
        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/"),
            Respond(status, "nope"));

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.HttpError));
        Assert.That(response.Status, Is.EqualTo((int)status));
        Assert.That(response.Succeeded, Is.False);
    }

    [Test]
    public async Task ARedirectToAnotherHostIsFollowedAsync() {
        StubHttpMessageHandler handler = new(request => request.RequestUri!.AbsolutePath == "/start"
            ? Redirect("https://elsewhere.example/next")
            : Respond(HttpStatusCode.OK, "arrived")(request));

        HttpFetchResponse response = await FetchAsync(Request("https://api.example.com/start"), handler);

        Assert.That(response.Succeeded, Is.True);
        Assert.That(response.Body, Is.EqualTo("arrived"));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ARedirectToANonHttpTargetIsNotFollowedAsync() {
        StubHttpMessageHandler handler = new(_ => Redirect("ftp://elsewhere.example/"));

        HttpFetchResponse response = await FetchAsync(Request("https://api.example.com/start"), handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.InvalidRequest));
        Assert.That(response.Status, Is.EqualTo(302));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(response.Headers["Location"], Is.EqualTo("ftp://elsewhere.example/"));
    }

    [Test]
    public async Task ExceedingTheRedirectLimitIsReportedAsync() {
        StubHttpMessageHandler handler = new(request => Redirect("/again"));

        HttpFetchResponse response = await FetchAsync(Request("https://api.example.com/start"), handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.TooManyRedirects));
        Assert.That(handler.Requests, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task ASeeOtherTurnsAPostIntoAGetAsync() {
        bool sawGet = false;
        StubHttpMessageHandler handler = new(request => {
            if (request.RequestUri!.AbsolutePath == "/after") {
                sawGet = request.Method == HttpMethod.Get && request.Content is null;
            }

            return request.RequestUri!.AbsolutePath == "/post"
                ? Redirect("/after", HttpStatusCode.SeeOther)
                : Respond(HttpStatusCode.OK, "after")(request);
        });

        HttpFetchResponse response = await FetchAsync(
            new HttpFetchRequest("https://api.example.com/post") { Method = HttpFetchMethod.Post, Body = "x" },
            handler);

        Assert.That(response.Succeeded, Is.True);
        Assert.That(sawGet, Is.True);
    }

    [Test]
    public async Task ATemporaryRedirectKeepsTheMethodAsync() {
        bool keptPost = false;
        StubHttpMessageHandler handler = new(request => {
            if (request.RequestUri!.AbsolutePath == "/after") {
                keptPost = request.Method == HttpMethod.Post;
            }

            return request.RequestUri!.AbsolutePath == "/post"
                ? Redirect("/after", HttpStatusCode.TemporaryRedirect)
                : Respond(HttpStatusCode.OK, "after")(request);
        });

        await FetchAsync(
            new HttpFetchRequest("https://api.example.com/post") { Method = HttpFetchMethod.Post, Body = "x" },
            handler);

        Assert.That(keptPost, Is.True);
    }

    [Test]
    public async Task ALongResponseIsTruncatedAtTheCapAsync() {
        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/"),
            Respond(HttpStatusCode.OK, new string('x', 4096)),
            new PageFetchOptions { MaxResponseBytes = 1024 });

        Assert.That(response.Truncated, Is.True);
        Assert.That(response.Body, Has.Length.EqualTo(1024));
        Assert.That(response.Succeeded, Is.True);
    }

    [Test]
    public async Task ANetworkFailureIsAValueAsync() {
        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/"),
            new StubHttpMessageHandler(_ => throw new HttpRequestException("refused")));

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.Network));
        Assert.That(response.Status, Is.Zero);
    }

    [Test]
    public async Task ThePerFetchTimeoutIsATimeoutAsync() {
        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/"),
            new SlowHandler(),
            new PageFetchOptions { TimeoutSeconds = 0.25 });

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.Timeout));
    }

    [Test]
    public async Task AnAbortedVisitorIsReportedAsAbortedAsync() {
        using CancellationTokenSource aborted = new();
        aborted.CancelAfter(50);

        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/"),
            new SlowHandler(),
            new PageFetchOptions { TimeoutSeconds = 30 },
            aborted.Token);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.Aborted));
    }

    [Test]
    public async Task TheRenderDeadlineIsATimeoutAsync() {
        using CancellationTokenSource deadline = new();
        deadline.CancelAfter(50);

        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/"),
            new SlowHandler(),
            new PageFetchOptions { TimeoutSeconds = 30 },
            CancellationToken.None,
            deadline.Token);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.Timeout));
    }

    [Test]
    public async Task AFetchIsRecordedWithItsOutcomeAsync() {
        using MetricCapture capture = MetricCapture.Start();
        PageMetrics metrics = new();
        PageFetcher fetcher = Fetcher(
            new StubHttpMessageHandler(Respond(HttpStatusCode.OK, "ok")),
            new PageFetchOptions(),
            metrics);

        await fetcher.FetchAsync(Request("https://api.example.com/"), "/page", CancellationToken.None, CancellationToken.None);

        Assert.That(capture.Count("page.fetch.duration", "outcome", PageMetrics.FetchOk), Is.EqualTo(1));
        Assert.That(capture.For("page.fetch.duration", "/page"), Has.Count.EqualTo(1));
    }

    [Test]
    public void FetchLimitsComeFromConfiguration() {
        ConfigurationBuilder builder = new();
        builder.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Pages:Fetch:TimeoutSeconds"] = "2.5",
            ["Pages:Fetch:MaxResponseBytes"] = "2048",
            ["Pages:Fetch:MaxRedirects"] = "0",
        });

        PageFetchOptions options = PageFetchOptions.FromConfiguration(builder.Build());

        Assert.Multiple(() => {
            Assert.That(options.TimeoutSeconds, Is.EqualTo(2.5));
            Assert.That(options.MaxResponseBytes, Is.EqualTo(2048));
            Assert.That(options.MaxRedirects, Is.Zero);
        });
    }

    private static HttpFetchRequest Request(string url) => new(url);

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string body) {
        return _ => new HttpResponseMessage(status) { Content = Content(body) };
    }

    private static StringContent Content(string body) => new(body, Encoding.UTF8, "text/plain");

    private static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found) {
        HttpResponseMessage message = new(status);
        message.Headers.TryAddWithoutValidation("Location", location);

        return message;
    }

    private static async Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        PageFetchOptions? options = null,
        CancellationToken aborted = default,
        CancellationToken deadline = default) {
        StubHttpMessageHandler handler = new(respond);
        PageFetcher fetcher = Fetcher(handler, options ?? new PageFetchOptions());

        return await fetcher.FetchAsync(request, "/page", aborted, deadline);
    }

    private static async Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        HttpMessageHandler handler,
        PageFetchOptions? options = null,
        CancellationToken aborted = default,
        CancellationToken deadline = default) {
        PageFetcher fetcher = Fetcher(handler, options ?? new PageFetchOptions());

        return await fetcher.FetchAsync(request, "/page", aborted, deadline);
    }

    private static PageFetcher Fetcher(HttpMessageHandler handler, PageFetchOptions options, PageMetrics? metrics = null) {
        return new PageFetcher(
            new OutboundFetcher(
                new StubHttpClientFactory(handler),
                options,
                NullLogger<OutboundFetcher>.Instance),
            metrics ?? new PageMetrics());
    }

    private sealed class SlowHandler : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
