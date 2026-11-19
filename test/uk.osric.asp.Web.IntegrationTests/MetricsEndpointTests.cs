// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

/// <summary>Proves the scrape endpoint exports our meters and shadows a page at its own key.</summary>
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

        string body = await ScrapeUntilAsync(client, "page_render_duration_seconds_bucket");

        Assert.That(body, Does.Contain("page_render_duration_seconds_bucket"));
    }

    [Test]
    public async Task MachineTrafficIsNotCountedInRequestDuration() {
        await SavePageAsync("/measured-health-probe", "Hello.");
        using HttpClient client = CreateClient();
        // A measured request first: the histogram is exported only once something has been observed,
        // so the absence of the machine routes would otherwise say nothing.
        await client.GetAsync("/measured-health-probe");
        await client.GetAsync("/health");

        string body = await ScrapeUntilAsync(client, "http_server_request_duration_seconds_bucket");

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain("http_server_request_duration_seconds_bucket"),
                "the hosting duration histogram has to be exported for the exclusions to mean anything");
            Assert.That(body, Does.Not.Contain("http_route=\"/health\""));
            Assert.That(body, Does.Not.Contain("http_route=\"/metrics\""));
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

        string body = await ScrapeUntilAsync(client, "page_render_duration_seconds_bucket");

        Assert.That(body, Does.Contain("page_render_duration_seconds_bucket"));
        Assert.That(body, Does.Not.Contain("not the scrape endpoint"));
    }

    // A scrape can be answered from a response cached for 300 ms, and a render's observation lands
    // only after its own response is built; polling keeps these tests off that race.
    private static async Task<string> ScrapeUntilAsync(HttpClient client, string family) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        string body;
        do {
            body = await client.GetStringAsync("/metrics");
            if (body.Contains(family, StringComparison.Ordinal)) {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        } while (DateTimeOffset.UtcNow < deadline);

        return body;
    }
}
