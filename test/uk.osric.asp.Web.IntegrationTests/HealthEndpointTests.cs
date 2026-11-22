// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

[TestFixture]
public sealed class HealthEndpointTests : IntegrationTest {

    [Test]
    public async Task HealthReportsDetailedJsonWhenHealthy() {
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("https://localhost/health");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        Assert.That(body, Does.Contain("\"status\":\"Healthy\""));
        Assert.That(body, Does.Contain("\"storage\""));
        Assert.That(body, Does.Contain("\"durationMs\""));
    }

    [Test]
    public async Task HealthRouteShadowsAPageStoredAtTheSameKey() {
        await SavePageAsync("/health", "<p>not the health check</p>");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("https://localhost/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
    }
}
