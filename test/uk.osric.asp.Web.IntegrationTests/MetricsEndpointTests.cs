// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using NUnit.Framework;
using System.Net;

/// <summary>Proves the scrape endpoint exports our meter with the buckets the dashboard reads.</summary>
[TestFixture]
public sealed class MetricsEndpointTests : IntegrationTest {

    [Test]
    public async Task RenderingAPageExposesTheRenderHistogram() {
        await SavePageAsync("/measured", "Hello.");
        using HttpClient client = CreateClient();
        await client.GetAsync("/measured");

        HttpResponseMessage response = await client.GetAsync("/metrics");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("page_render_duration_seconds_bucket"));
    }

    [Test]
    public async Task TheExpositionCarriesOurBucketBoundaries() {
        await SavePageAsync("/bucketed", "Hello.");
        using HttpClient client = CreateClient();
        await client.GetAsync("/bucketed");

        string body = await (await client.GetAsync("/metrics")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("le=\"0.0005\""));
        Assert.That(body, Does.Contain("le=\"10\""));
        Assert.That(body, Does.Not.Contain("le=\"7500\""), "the generic OTel bounds must be replaced by a view");
    }

    [Test]
    public async Task MetricsRouteShadowsAPageStoredAtTheSameKey() {
        await SavePageAsync("/metrics", "<p>not the scrape endpoint</p>");
        using HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/metrics");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("page_render_duration_seconds_bucket"));
        Assert.That(body, Does.Not.Contain("not the scrape endpoint"));
    }
}
