// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

/// <summary>Caddy terminates TLS, so the request scheme only becomes honest once its headers are trusted.</summary>
[TestFixture]
public sealed class ForwardedHeadersTests : IntegrationTest {

    // HSTS skips its default excluded hosts, which include localhost, so the request names a host
    // Caddy would serve rather than the one the test client connects to.
    private const string ProxiedUrl = "http://example.com/missing.cs";

    [Test]
    public async Task ForwardedProtoMakesHstsApply() {
        using HttpClient client = CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, ProxiedUrl);
        request.Headers.Add("X-Forwarded-Proto", "https");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Headers.Contains("Strict-Transport-Security"), Is.True);
    }

    [Test]
    public async Task PlainHttpRequestGetsNoHstsHeader() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync(ProxiedUrl);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Headers.Contains("Strict-Transport-Security"), Is.False);
    }
}
