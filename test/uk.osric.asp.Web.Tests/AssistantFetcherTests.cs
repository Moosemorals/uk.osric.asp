// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class AssistantFetcherTests {

    [Test]
    public async Task AGetRequestCarriesTheUrlAsync() {
        StubHttpMessageHandler handler = new(request => {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://example.com/status")));

            return Respond(HttpStatusCode.OK, "up", "text/plain; charset=utf-8")(request);
        });

        HttpFetchResponse response = await FetchAsync("https://example.com/status", handler);

        Assert.Multiple(() => {
            Assert.That(response.Succeeded, Is.True);
            Assert.That(response.Status, Is.EqualTo(200));
            Assert.That(response.Body, Is.EqualTo("up"));
            Assert.That(response.Headers["Content-Type"], Is.EqualTo("text/plain; charset=utf-8"));
            Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.None));
        });
    }

    [Test]
    public async Task AnEmptyUrlIsInvalidWithoutASocketAsync() {
        StubHttpMessageHandler handler = new(Respond(HttpStatusCode.OK, "no"));

        HttpFetchResponse response = await FetchAsync(" ", handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.InvalidRequest));
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase("ftp://example.com/")]
    [TestCase("mailto:someone@example.com")]
    [TestCase("https://user:pass@example.com/")]
    [TestCase("not a url")]
    public async Task AnUnacceptableUrlIsInvalidWithoutASocketAsync(string url) {
        StubHttpMessageHandler handler = new(Respond(HttpStatusCode.OK, "no"));

        HttpFetchResponse response = await FetchAsync(url, handler);

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.InvalidRequest));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task TheAssistantUserAgentIsSentAsync() {
        StubHttpMessageHandler handler = new(request => {
            Assert.That(request.Headers.UserAgent.ToString(), Is.EqualTo("moosemorals.com (assistant)"));

            return Respond(HttpStatusCode.OK, "ok")(request);
        });

        HttpFetchResponse response = await FetchAsync("https://example.com/", handler);

        Assert.That(response.Succeeded, Is.True);
    }

    [Test]
    public async Task A4xxIsAnHttpErrorWithTheRealStatusAsync() {
        HttpFetchResponse response = await FetchAsync(
            "https://example.com/missing",
            new StubHttpMessageHandler(Respond(HttpStatusCode.NotFound, "missing")));

        Assert.Multiple(() => {
            Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.HttpError));
            Assert.That(response.Status, Is.EqualTo(404));
            Assert.That(response.Body, Is.EqualTo("missing"));
        });
    }

    [Test]
    public async Task ARedirectToAnotherHostIsFollowedAsync() {
        StubHttpMessageHandler handler = new(request => request.RequestUri!.AbsolutePath == "/start"
            ? Redirect("https://elsewhere.example/next")
            : Respond(HttpStatusCode.OK, "arrived")(request));

        HttpFetchResponse response = await FetchAsync("https://example.com/start", handler);

        Assert.Multiple(() => {
            Assert.That(response.Succeeded, Is.True);
            Assert.That(response.Body, Is.EqualTo("arrived"));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ANetworkFailureIsAValueAsync() {
        HttpFetchResponse response = await FetchAsync(
            "https://example.com/",
            new StubHttpMessageHandler(_ => throw new HttpRequestException("refused")));

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.Network));
        Assert.That(response.Status, Is.Zero);
    }

    [Test]
    public async Task TheResponseCapIsAppliedAsync() {
        HttpFetchResponse response = await FetchAsync(
            "https://example.com/",
            new StubHttpMessageHandler(Respond(HttpStatusCode.OK, new string('x', 4096))),
            TestOptions.Fetch with { MaxResponseBytes = 1024 });

        Assert.Multiple(() => {
            Assert.That(response.Truncated, Is.True);
            Assert.That(response.Body, Has.Length.EqualTo(1024));
        });
    }

    [Test]
    public async Task ThePerFetchTimeoutIsAValueAsync() {
        HttpFetchResponse response = await FetchAsync(
            "https://example.com/",
            new SlowHandler(),
            TestOptions.Fetch with { TimeoutSeconds = 0.25 });

        Assert.That(response.Failure, Is.EqualTo(HttpFetchFailure.Timeout));
    }

    private static async Task<HttpFetchResponse> FetchAsync(
        string url,
        HttpMessageHandler handler,
        PageFetchOptions? options = null) {
        AssistantFetcher fetcher = new(new OutboundFetcher(
            new StubHttpClientFactory(handler),
            options ?? TestOptions.Fetch,
            NullLogger<OutboundFetcher>.Instance));

        return await fetcher.FetchAsync(url, CancellationToken.None);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Respond(
        HttpStatusCode status,
        string body,
        string contentType = "text/plain") {
        return _ => {
            HttpResponseMessage message = new(status) {
                Content = new StringContent(body, Encoding.UTF8),
            };
            message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

            return message;
        };
    }

    private static HttpResponseMessage Redirect(string location) {
        HttpResponseMessage message = new(HttpStatusCode.Found);
        message.Headers.TryAddWithoutValidation("Location", location);

        return message;
    }

    private sealed class SlowHandler : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
