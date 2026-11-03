// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class StatusEndpointTests {

    private static readonly MetricsOptions Options = new() {
        PrometheusUrl = new Uri("http://prometheus:9090"),
        TimeZone = TimeZoneInfo.Utc,
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-02T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public async Task ChartsAndALegendAreWrittenForAnAnswer() {
        string body = await RenderAsync(new MetricsMatrix([
            new ChartSeries("ok", [
                new ChartPoint(Now.AddMinutes(-30), 1),
                new ChartPoint(Now, 2),
            ]),
        ]));

        Assert.That(body, Does.Contain("<h1>Status</h1>"));
        Assert.That(body, Does.Contain("<svg class=\"chart\""));
        Assert.That(body, Does.Contain("<svg class=\"heatmap\""));
        Assert.That(body, Does.Contain("<ul class=\"chart-legend\">"));
        Assert.That(body, Does.Contain("series-1"));
    }

    [Test]
    public async Task AFailureIsABanner() {
        string body = await RenderAsync(new MetricsUnavailable("Prometheus is unreachable."));

        Assert.That(body, Does.Contain("class=\"banner\""));
        Assert.That(body, Does.Contain("Prometheus is unreachable."));
        Assert.That(body, Does.Not.Contain("<svg class=\"chart\""));
    }

    [Test]
    public async Task EveryLegendLabelIsEscaped() {
        string body = await RenderAsync(new MetricsMatrix([
            new ChartSeries("<script>", [
                new ChartPoint(Now.AddMinutes(-30), 1),
                new ChartPoint(Now, 2),
            ]),
        ]));

        Assert.That(body, Does.Contain("&lt;script&gt;"));
        Assert.That(body, Does.Not.Contain("<script>"));
    }

    [Test]
    public async Task TheNavLinksEveryWindowButTheCurrentOne() {
        string body = await RenderAsync(new MetricsMatrix([
            new ChartSeries("ok", [
                new ChartPoint(Now.AddMinutes(-30), 1),
                new ChartPoint(Now, 2),
            ]),
        ]));

        Assert.That(body, Does.Contain("<nav class=\"status-nav\">"));

        foreach (string token in (string[])["3h", "8h", "1d", "7d", "30d"]) {
            Assert.That(body, Does.Contain($"<a href=\"/status?duration={token}\">{token}</a>"));
        }

        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">1h</span>"));
        Assert.That(body, Does.Not.Contain("href=\"/status?duration=1h\""));
    }

    [Test]
    public async Task TheRequestedWindowIsTheOneNotLinked() {
        string body = await RenderAsync(
            new MetricsMatrix([
                new ChartSeries("ok", [
                    new ChartPoint(Now.AddDays(-3), 1),
                    new ChartPoint(Now, 2),
                ]),
            ]),
            "?duration=7d");

        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">7d</span>"));
        Assert.That(body, Does.Contain("href=\"/status?duration=1h\""));
        Assert.That(body, Does.Not.Contain("href=\"/status?duration=7d\""));
    }

    [Test]
    public async Task AnUnknownWindowFallsBackToTheDefault() {
        string body = await RenderAsync(
            new MetricsMatrix([
                new ChartSeries("ok", [
                    new ChartPoint(Now.AddMinutes(-30), 1),
                    new ChartPoint(Now, 2),
                ]),
            ]),
            "?duration=2w");

        Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">1h</span>"));
    }

    private static async Task<string> RenderAsync(MetricsQueryResult answer, string? queryString = null) {
        MetricsDashboard dashboard = new(
            new FakeMetricsSource(_ => answer),
            Options,
            new TestClock { Now = Now },
            NullLogger<MetricsDashboard>.Instance);
        HttpContext context = TestHttp.CreateContext(null);
        context.Request.QueryString = new QueryString(queryString);

        await StatusEndpoint.HandleAsync(context, dashboard, CancellationToken.None);

        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        return await TestHttp.ReadBodyAsync(context);
    }
}
