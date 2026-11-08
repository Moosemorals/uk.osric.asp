// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net;
using System.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageFetcherTests {

    [Test]
    public async Task AnAllowedHostIsFetchedAsync() {
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
    public async Task AHostThatIsNotListedIsRefusedWithoutASocketAsync() {
        StubHttpMessageHandler handler = new(Respond(HttpStatusCode.OK, "no"));

        HttpFetchResponse response = await FetchAsync(Request("https://evil.example/"), handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.NotAllowed));
        Assert.That(response.Status, Is.Zero);
        Assert.That(handler.Requests, Is.Empty);
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
    public async Task ARedirectToAnAllowedHostIsFollowedAsync() {
        StubHttpMessageHandler handler = new(request => request.RequestUri!.AbsolutePath == "/start"
            ? Redirect("/next")
            : Respond(HttpStatusCode.OK, "arrived")(request));

        HttpFetchResponse response = await FetchAsync(Request("https://api.example.com/start"), handler);

        Assert.That(response.Succeeded, Is.True);
        Assert.That(response.Body, Is.EqualTo("arrived"));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ARedirectToADisallowedHostIsNotFollowedAsync() {
        StubHttpMessageHandler handler = new(_ => {
            HttpResponseMessage message = Redirect("https://elsewhere.example/");
            return message;
        });

        HttpFetchResponse response = await FetchAsync(Request("https://api.example.com/start"), handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.NotAllowed));
        Assert.That(response.Status, Is.EqualTo(302));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(response.Headers["Location"], Is.EqualTo("https://elsewhere.example/"));
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
            new PageFetchOptions { AllowedHosts = ["api.example.com"], MaxResponseBytes = 1024 });

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
            new PageFetchOptions { AllowedHosts = ["api.example.com"], TimeoutSeconds = 0.25 });

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.Timeout));
    }

    [Test]
    public async Task AnAbortedVisitorIsReportedAsAbortedAsync() {
        using CancellationTokenSource aborted = new();
        aborted.CancelAfter(50);

        HttpFetchResponse response = await FetchAsync(
            Request("https://api.example.com/"),
            new SlowHandler(),
            new PageFetchOptions { AllowedHosts = ["api.example.com"], TimeoutSeconds = 30 },
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
            new PageFetchOptions { AllowedHosts = ["api.example.com"], TimeoutSeconds = 30 },
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
            new PageFetchOptions { AllowedHosts = ["api.example.com"] },
            metrics);

        await fetcher.FetchAsync(Request("https://api.example.com/"), "/page", CancellationToken.None, CancellationToken.None);

        Assert.That(capture.Count("page.fetch.duration", "outcome", PageMetrics.FetchOk), Is.EqualTo(1));
        Assert.That(capture.For("page.fetch.duration", "/page"), Has.Count.EqualTo(1));
    }

    [TestCase("")]
    [TestCase("https://api.example.com/")]
    [TestCase("api.example.com/path")]
    [TestCase("api.example.com:8443")]
    public void AMalformedAllowedHostIsAStartupError(string host) {
        ConfigurationBuilder builder = new();
        builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Pages:Fetch:AllowedHosts:0"] = host });

        Assert.Throws<InvalidOperationException>(() => PageFetchOptions.FromConfiguration(builder.Build()));
    }

    [Test]
    public void ABracketedIpv6HostIsAccepted() {
        ConfigurationBuilder builder = new();
        builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Pages:Fetch:AllowedHosts:0"] = "[::1]" });

        PageFetchOptions options = PageFetchOptions.FromConfiguration(builder.Build());

        Assert.That(options.Allows("[::1]"), Is.True);
    }

    [Test]
    public void HostsMatchExactlyAndCaseInsensitively() {
        PageFetchOptions options = new() { AllowedHosts = ["API.example.com"] };

        Assert.That(options.Allows("api.example.com"), Is.True);
        Assert.That(options.Allows("api.example.com.evil"), Is.False);
        Assert.That(options.Allows("sub.api.example.com"), Is.False);
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
        PageFetcher fetcher = Fetcher(handler, options ?? new PageFetchOptions { AllowedHosts = ["api.example.com"] });

        return await fetcher.FetchAsync(request, "/page", aborted, deadline);
    }

    private static async Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        HttpMessageHandler handler,
        PageFetchOptions? options = null,
        CancellationToken aborted = default,
        CancellationToken deadline = default) {
        PageFetcher fetcher = Fetcher(handler, options ?? new PageFetchOptions { AllowedHosts = ["api.example.com"] });

        return await fetcher.FetchAsync(request, "/page", aborted, deadline);
    }

    private static PageFetcher Fetcher(HttpMessageHandler handler, PageFetchOptions options, PageMetrics? metrics = null) {
        return new PageFetcher(
            new StubHttpClientFactory(handler),
            options,
            metrics ?? new PageMetrics(),
            NullLogger<PageFetcher>.Instance);
    }

    private sealed class SlowHandler : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory {
        // A fresh client per call, because the fetcher disposes the one it is given.
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
