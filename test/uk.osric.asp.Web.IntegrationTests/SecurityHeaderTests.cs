// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;
using uk.osric.asp.Web.Http;

[TestFixture]
public sealed class SecurityHeaderTests : IntegrationTest {

    private static readonly string[] HeaderNames = [
        SecurityHeaders.ContentSecurityPolicy,
        "X-Content-Type-Options",
        "X-Frame-Options",
        "Cross-Origin-Opener-Policy",
        "Cross-Origin-Resource-Policy",
        "Referrer-Policy",
        "Permissions-Policy",
        "X-XSS-Protection",
    ];

    [Test]
    public async Task TheEditorViewAllowsItsOwnModule() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage response = await client.GetAsync("/edit/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertEveryHeader(response);
        Assert.That(Policy(response), Is.EqualTo(SecurityHeaders.WithScripts));
        Assert.That(response.Headers.GetValues("X-Frame-Options"), Is.EqualTo(new List<string> { "DENY" }),
            "generating an antiforgery token must not widen this to SAMEORIGIN");
    }

    [Test]
    public async Task TheStatusPageAndANotFoundForbidScripts() {
        using HttpClient client = await CreateSignedInClientAsync();

        HttpResponseMessage status = await client.GetAsync("/status");
        Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertEveryHeader(status);
        Assert.That(Policy(status), Is.EqualTo(SecurityHeaders.Default));

        HttpResponseMessage missing = await client.GetAsync("/missing-page");
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        AssertEveryHeader(missing);
        Assert.That(Policy(missing), Is.EqualTo(SecurityHeaders.Default));
    }

    [Test]
    public async Task AnErrorPageForbidsScripts() {
        await SavePageAsync("/boom", "<% throw new System.InvalidOperationException(\"Boom.\"); %>");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/boom");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        AssertEveryHeader(response);
        Assert.That(Policy(response), Is.EqualTo(SecurityHeaders.Default));
    }

    [Test]
    public async Task AnAssetPageForbidsScriptsAndKeepsItsContentType() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/site.css");

        Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/css; charset=utf-8"));
        AssertEveryHeader(response);
        Assert.That(Policy(response), Is.EqualTo(SecurityHeaders.Default));
    }

    [Test]
    public async Task ASeededScriptKeepsItsTypeAndForbidsInlineScripts() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/assistant.js");

        Assert.That(response.Content.Headers.ContentType?.ToString(), Is.EqualTo("text/javascript; charset=utf-8"));
        AssertEveryHeader(response);
        Assert.That(Policy(response), Is.EqualTo(SecurityHeaders.Default),
            "a JavaScript response does not widen its own policy");
    }

    [Test]
    public async Task APageThatAllowsScriptsWidensItsPolicy() {
        await SavePageAsync("/scripted", "<% ctx.AllowScripts(); %>Scripted.");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/scripted");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertEveryHeader(response);
        Assert.That(Policy(response), Is.EqualTo(SecurityHeaders.WithScripts));
    }

    [Test]
    public async Task APageThatDoesNotAllowScriptsForbidsThem() {
        await SavePageAsync("/plain", "Plain.");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/plain");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertEveryHeader(response);
        Assert.That(Policy(response), Is.EqualTo(SecurityHeaders.Default));
    }

    [Test]
    public async Task APageThatThrowsAfterAllowingScriptsStillForbidsThem() {
        await SavePageAsync("/boom-scripted",
            "<% ctx.AllowScripts(); throw new System.InvalidOperationException(\"Boom.\"); %>");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/boom-scripted");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        AssertEveryHeader(response);
        Assert.That(Policy(response), Is.EqualTo(SecurityHeaders.Default));
    }

    [Test]
    public async Task ANotModifiedScriptedPageStillCarriesItsPolicy() {
        await SavePageAsync("/scripted-cached", "<% ctx.AllowScripts(); %>Scripted.");
        using HttpClient client = CreateClient();
        HttpResponseMessage first = await client.GetAsync("/scripted-cached");
        Assert.That(first.Headers.ETag, Is.Not.Null);

        using HttpRequestMessage request = new(HttpMethod.Get, "/scripted-cached");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        HttpResponseMessage notModified = await client.SendAsync(request);

        Assert.That(notModified.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
        AssertEveryHeader(notModified);
        Assert.That(Policy(notModified), Is.EqualTo(SecurityHeaders.WithScripts));
    }

    private static string Policy(HttpResponseMessage response) {
        Assert.That(response.Headers.TryGetValues(SecurityHeaders.ContentSecurityPolicy, out IEnumerable<string>? values), Is.True);
        Assert.That(values, Has.Exactly(1).Items, "one value, never a list");

        return values!.Single();
    }

    private static void AssertEveryHeader(HttpResponseMessage response) {
        foreach (string name in HeaderNames) {
            Assert.That(response.Headers.TryGetValues(name, out IEnumerable<string>? values), Is.True, name);
            Assert.That(values, Has.Exactly(1).Items, name);
        }
    }
}
