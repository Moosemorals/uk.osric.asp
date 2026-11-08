// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using System.Net;

/// <summary>Proves the scrape endpoint exports our meter with the buckets the dashboard reads.</summary>
[TestFixture]
public sealed class MetricsEndpointTests : IntegrationTest {

    [Test]
    public async Task StorageGaugesExportBytesWithPreciseAccountingDescriptionsAsync() {
        using HttpClient client = CreateClient();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        string body;
        do {
            body = await client.GetStringAsync("/metrics", deadline.Token);
            if (body.Contains("page_storage_volume_capacity_bytes", StringComparison.Ordinal)) {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
        } while (!deadline.IsCancellationRequested);

        Assert.That(body, Does.Contain("# TYPE page_storage_payload_size_bytes gauge"));
        Assert.That(body, Does.Contain("# TYPE page_storage_database_size_bytes gauge"));
        Assert.That(body, Does.Contain("# TYPE page_storage_volume_capacity_bytes gauge"));
        Assert.That(body, Does.Contain("excludes indexes and SQLite overhead"));
        Assert.That(body, Does.Contain("including indexes and freelist; excludes separate WAL"));
        Assert.That(body, Does.Not.Contain("page_storage_payload_size_bytes{page_path="));
    }

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

        // The exporter caches a scrape for 300 ms, and this fixture scrapes more than once; wait for
        // the render to reach the exposition rather than racing the cache.
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        string body;
        do {
            body = await client.GetStringAsync("/metrics", deadline.Token);
            if (body.Contains("le=\"0.0005\"", StringComparison.Ordinal)) {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
        } while (!deadline.IsCancellationRequested);

        Assert.That(body, Does.Contain("le=\"0.0005\""));
        Assert.That(body, Does.Contain("le=\"10\""));
        Assert.That(body, Does.Not.Contain("le=\"7500\""), "the generic OTel bounds must be replaced by a view");
    }

    [Test]
    public async Task TheLoadAndInvokeHistogramsCarryOurBucketBoundaries() {
        await SavePageAsync("/bucketed-load", "Hello.");
        using HttpClient client = CreateClient();
        await client.GetAsync("/bucketed-load");

        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        string body;
        do {
            body = await client.GetStringAsync("/metrics", deadline.Token);
            if (body.Contains("page_load_duration_seconds_bucket", StringComparison.Ordinal)
                && body.Contains("page_invoke_duration_seconds_bucket", StringComparison.Ordinal)) {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
        } while (!deadline.IsCancellationRequested);

        Assert.Multiple(() => {
            Assert.That(BucketsOf(body, "page_load_duration_seconds_bucket"), Does.Contain("0.0005"));
            Assert.That(BucketsOf(body, "page_invoke_duration_seconds_bucket"), Does.Contain("0.0005"));
            Assert.That(BucketsOf(body, "page_load_duration_seconds_bucket"), Does.Not.Contain("7500"));
            Assert.That(BucketsOf(body, "page_invoke_duration_seconds_bucket"), Does.Not.Contain("7500"));
        });
    }

    [Test]
    public async Task MetricsRouteShadowsAPageStoredAtTheSameKey() {
        await SavePageAsync("/measured", "Hello.");
        await SavePageAsync("/metrics", "<p>not the scrape endpoint</p>");
        using HttpClient client = CreateClient();
        // The histogram only appears once something has been observed, so render a page first
        // rather than relying on another test in the fixture having done so.
        await client.GetAsync("/measured");

        HttpResponseMessage response = await client.GetAsync("/metrics");
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("page_render_duration_seconds_bucket"));
        Assert.That(body, Does.Not.Contain("not the scrape endpoint"));
    }

    private static IEnumerable<string> BucketsOf(string exposition, string series) =>
        exposition.Split('\n')
            .Where(line => line.StartsWith(series, StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf("le=\"", StringComparison.Ordinal) + 4)..].Split('"')[0]);
}
