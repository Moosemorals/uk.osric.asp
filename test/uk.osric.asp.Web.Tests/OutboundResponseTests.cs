// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>
/// The transport's own answer, and the page's projection of it: a view needs the response, a page
/// needs text.
/// </summary>
[TestFixture]
public sealed class OutboundResponseTests {

    [Test]
    public async Task EveryResponseHeaderComesBackAndAPageStillSeesTheAllowList() {
        // Each fetch consumes the response it is given, so the handler builds one per request.
        StubHttpMessageHandler handler = new(_ => {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StringContent("hi", Encoding.UTF8) };
            response.Headers.TryAddWithoutValidation("Set-Cookie", "s=abc; HttpOnly");
            response.Headers.TryAddWithoutValidation("X-Frame-Options", "DENY");
            response.Content.Headers.TryAddWithoutValidation("Content-Security-Policy", "default-src 'none'");
            response.Content.Headers.ContentLength = 2;

            return response;
        });

        OutboundResponse view = await FetchResponseAsync("https://example.com/x", handler);
        HttpFetchResponse page = await FetchAsync("https://example.com/x", handler);

        Assert.Multiple(() => {
            Assert.That(view.Headers.Keys, Does.Contain("Set-Cookie"));
            Assert.That(view.Headers.Keys, Does.Contain("X-Frame-Options"));
            Assert.That(view.Headers.Keys, Does.Contain("Content-Security-Policy"));
            Assert.That(view.Headers.Keys, Does.Contain("Content-Type"));
            Assert.That(view.Headers.Keys, Does.Contain("Content-Length"));
            Assert.That(page.Headers.Keys, Does.Not.Contain("Set-Cookie"));
            Assert.That(page.Headers.Keys, Does.Not.Contain("Content-Security-Policy"));
            Assert.That(page.Headers.Keys, Does.Not.Contain("Content-Length"), "an X- header is allowed, a content header is not");
        });
    }

    [Test]
    public async Task ABodyDeclaredAsLatin1IsStillCarriedAsItsOwnBytes() {
        // The host serves every GET as UTF-8; a page that declares another charset is declaring a lie.
        // A view must show the bytes rather than decode by the claim, which is what makes it visible.
        byte[] utf8 = Encoding.UTF8.GetBytes("café");
        StubHttpMessageHandler handler = new(_ => Raw(HttpStatusCode.OK, utf8, "text/plain; charset=iso-8859-1"));

        OutboundResponse view = await FetchResponseAsync("https://example.com/x", handler);
        HttpFetchResponse page = await FetchAsync("https://example.com/x", handler);

        Assert.Multiple(() => {
            Assert.That(view.Body, Is.EqualTo(utf8));
            Assert.That(Encoding.UTF8.GetString(view.Body), Is.EqualTo("café"));
            // The page's projection is unchanged: it believes the declared charset, as it always has.
            Assert.That(page.Body, Is.EqualTo("cafÃ©"));
        });
    }

    [Test]
    public async Task ABodyThatIsNotUtf8IsCarriedAsItsBytesRatherThanReplacementCharacters() {
        byte[] binary = [0xFF, 0xFE, 0x00, 0x81];
        StubHttpMessageHandler handler = new(_ => Raw(HttpStatusCode.OK, binary, "application/octet-stream"));

        OutboundResponse view = await FetchResponseAsync("https://example.com/x", handler);

        Assert.That(view.Body, Is.EqualTo(binary));
    }

    [Test]
    public async Task AGotoLoginIsReportedAsAHopAsWellAsAStatus() {
        StubHttpMessageHandler handler = new(request => request.RequestUri!.AbsolutePath switch {
            "/pages" => Redirect("/login?returnUrl=/pages"),
            _ => Respond(HttpStatusCode.OK, "<form>sign in</form>"),
        });

        OutboundResponse view = await FetchResponseAsync("https://example.com/pages", handler);

        Assert.Multiple(() => {
            Assert.That(view.Status, Is.EqualTo(200));
            Assert.That(view.Redirects, Has.Count.EqualTo(1));
            Assert.That(view.Redirects[0].Status, Is.EqualTo(302));
            Assert.That(view.Redirects[0].Location, Is.EqualTo("https://example.com/login?returnUrl=/pages"));
        });
    }

    [Test]
    public async Task AResponseThatDoesNotRedirectReportsNoHops() {
        StubHttpMessageHandler handler = new(_ => Respond(HttpStatusCode.OK, "hi"));

        OutboundResponse view = await FetchResponseAsync("https://example.com/x", handler);

        Assert.That(view.Redirects, Is.Empty);
    }

    [Test]
    public async Task ACutBodyReportsTheLimitItHit() {
        StubHttpMessageHandler handler = new(_ => Respond(HttpStatusCode.OK, new string('x', 1000)));
        PageFetchOptions limited = TestOptions.Fetch with { MaxResponseBytes = 16 };

        OutboundResponse view = await FetchResponseAsync("https://example.com/x", handler, limited);

        Assert.Multiple(() => {
            Assert.That(view.Truncated, Is.True);
            Assert.That(view.Body, Has.Length.EqualTo(16));
        });
    }

    [Test]
    public async Task AWholeBodySaysItIsWhole() {
        StubHttpMessageHandler handler = new(_ => Respond(HttpStatusCode.OK, "short"));

        OutboundResponse view = await FetchResponseAsync("https://example.com/x", handler);

        Assert.Multiple(() => {
            Assert.That(view.Truncated, Is.False);
            Assert.That(Encoding.UTF8.GetString(view.Body), Is.EqualTo("short"));
        });
    }

    [Test]
    public async Task AViewCredentialIsAttachedToItsOriginAndNowhereElse() {
        ViewCredential credential = new(new Uri("http://127.0.0.1:8080"), "secret");

        Assert.Multiple(() => {
            Assert.That(credential.AppliesTo(new Uri("http://127.0.0.1:8080/pages?edit=/x")), Is.True);
            Assert.That(credential.AppliesTo(new Uri("http://127.0.0.1:9090/pages")), Is.False, "another port is another service");
            Assert.That(credential.AppliesTo(new Uri("https://127.0.0.1:8080/pages")), Is.False, "another scheme is another origin");
            Assert.That(credential.AppliesTo(new Uri("http://example.com:8080/pages")), Is.False);
            Assert.That(credential.ToString(), Does.Not.Contain("secret"));
        });
    }

    [Test]
    public void AViewCredentialRefusesToCarryAnythingOverLoopbackWithoutItsOrigin() {
        ViewCredential credential = new(new Uri("https://moosemorals.com"), "secret");

        Assert.Multiple(() => {
            Assert.That(credential.AppliesTo(new Uri("https://moosemorals.com/")), Is.True);
            Assert.That(credential.AppliesTo(new Uri("http://moosemorals.com/")), Is.False);
            Assert.That(credential.AppliesTo(new Uri("https://api.deepseek.com/")), Is.False);
        });
    }

    private static Task<OutboundResponse> FetchResponseAsync(
        string url,
        HttpMessageHandler handler,
        PageFetchOptions? options = null) {
        OutboundFetcher fetcher = new(
            new StubHttpClientFactory(handler),
            options ?? TestOptions.Fetch,
            NullLogger<OutboundFetcher>.Instance);

        return fetcher.FetchResponseAsync(
            new HttpFetchRequest(url), "the assistant", OutboundOrigin.Assistant, credential: null,
            aborted: CancellationToken.None, deadline: CancellationToken.None);
    }

    private static Task<HttpFetchResponse> FetchAsync(
        string url,
        HttpMessageHandler handler,
        PageFetchOptions? options = null) {
        OutboundFetcher fetcher = new(
            new StubHttpClientFactory(handler),
            options ?? TestOptions.Fetch,
            NullLogger<OutboundFetcher>.Instance);

        return fetcher.FetchAsync(
            new HttpFetchRequest(url), "the assistant", OutboundOrigin.Assistant,
            aborted: CancellationToken.None, deadline: CancellationToken.None);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body, string contentType = "text/plain") {
        HttpResponseMessage message = new(status) { Content = new StringContent(body, Encoding.UTF8) };
        message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        return message;
    }

    private static HttpResponseMessage Raw(HttpStatusCode status, byte[] body, string contentType) {
        HttpResponseMessage message = new(status) { Content = new ByteArrayContent(body) };
        message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        return message;
    }

    private static HttpResponseMessage Redirect(string location) {
        HttpResponseMessage message = new(HttpStatusCode.Found);
        message.Headers.TryAddWithoutValidation("Location", location);

        return message;
    }
}
