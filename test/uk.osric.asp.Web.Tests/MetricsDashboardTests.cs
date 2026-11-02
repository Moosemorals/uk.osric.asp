// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using uk.osric.asp.Core;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class MetricsDashboardTests {

    private static readonly MetricsOptions Options = new() {
        PrometheusUrl = new Uri("http://prometheus:9090"),
        TimeZone = TimeZoneInfo.Utc,
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-02T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static MetricsQueryResult Answer(string label = "ok") => new MetricsMatrix([
        new ChartSeries(label, [
            new ChartPoint(Now.AddMinutes(-30), 1),
            new ChartPoint(Now, 2),
        ]),
    ]);

    [Test]
    public void NoQueryUsesAnOpenTelemetryTagNameAsALabel() {
        // PromQL label names cannot contain dots: the exporter writes the tag page.path as page_path.
        Assert.That(MetricsDashboard.Queries, Is.All.Not.Contain("page.path"));
        Assert.That(MetricsDashboard.Queries, Has.Some.Contain("page_path"));
    }

    [Test]
    public async Task RequestsByPageIsTheFirstPanel() {
        MetricsDashboard dashboard = new(
            new FakeMetricsSource(_ => Answer()),
            Options,
            new TestClock { Now = Now },
            NullLogger<MetricsDashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(CancellationToken.None);

        Assert.That(page.Panels[0].Title, Is.EqualTo("Requests by page"));
    }

    [Test]
    public async Task AnAnonymousSeriesIsNamedAfterItsQuery() {
        MetricsDashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("", [new ChartPoint(Now.AddMinutes(-1), 1)]),
            ])),
            Options,
            new TestClock { Now = Now },
            NullLogger<MetricsDashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(CancellationToken.None);

        StatusPanel duration = page.Panels.Single(panel => panel.Title == "Render duration p50/p95");
        Assert.That(duration.Labels, Is.EqualTo((string[])["p50", "p95"]));
    }

    [Test]
    public async Task ASubSecondDurationAxisIsDrawnInMilliseconds() {
        MetricsDashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("", [
                    new ChartPoint(Now.AddMinutes(-30), 0.0002),
                    new ChartPoint(Now, 0.0004),
                ]),
            ])),
            Options,
            new TestClock { Now = Now },
            NullLogger<MetricsDashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(CancellationToken.None);

        StatusPanel duration = page.Panels.Single(panel => panel.Title == "Render duration p50/p95");
        Assert.That(duration.Svg, Does.Contain(">milliseconds<"));
        Assert.That(duration.Svg, Does.Not.Contain(">seconds<"));

        StatusPanel rate = page.Panels.Single(panel => panel.Title == "Cache lookups by result");
        Assert.That(rate.Svg, Does.Contain(">per second<"));
    }

    [Test]
    public async Task EveryPanelIsDrawnWhenTheSourceAnswers() {
        MetricsDashboard dashboard = new(new FakeMetricsSource(_ => Answer()), Options, new TestClock { Now = Now }, NullLogger<MetricsDashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(CancellationToken.None);

        Assert.That(page.Banner, Is.Null);
        Assert.That(page.Panels, Has.Count.EqualTo(6));
        Assert.That(page.Panels.Select(panel => panel.Svg), Is.All.Not.Null);
        Assert.That(page.Panels[0].Svg, Does.Contain("<svg class=\"chart\""));
        Assert.That(page.Panels[^1].Title, Is.EqualTo("Render duration buckets"));
        Assert.That(page.Panels[^1].Svg, Does.Contain("<svg class=\"heatmap\""));
    }

    [Test]
    public async Task ARemoteFailureBecomesABannerInsteadOfCharts() {
        MetricsDashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsUnavailable("Prometheus is unreachable.")),
            Options,
            new TestClock { Now = Now },
            NullLogger<MetricsDashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(CancellationToken.None);

        Assert.That(page.Banner, Is.EqualTo("Prometheus is unreachable."));
        Assert.That(page.Panels, Is.Empty);
    }

    [Test]
    public async Task OneFailedPanelLeavesTheOthers() {
        MetricsDashboard dashboard = new(
            new FakeMetricsSource(query => query.Contains("cache", StringComparison.Ordinal)
                ? new MetricsUnavailable("cache query failed")
                : Answer()),
            Options,
            new TestClock { Now = Now },
            NullLogger<MetricsDashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(CancellationToken.None);

        Assert.That(page.Banner, Is.EqualTo("cache query failed"));
        Assert.That(page.Panels, Has.Count.EqualTo(5));
        Assert.That(page.Panels.Any(panel => panel.Title == "Cache lookups by result"), Is.False);
    }

    [Test]
    public async Task AnEmptyAnswerIsReportedAsNoData() {
        MetricsDashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsMatrix([])),
            Options,
            new TestClock { Now = Now },
            NullLogger<MetricsDashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(CancellationToken.None);

        Assert.That(page.Panels, Has.Count.EqualTo(6));
        Assert.That(page.Panels.Select(panel => panel.Svg), Is.All.Null);
    }

    [Test]
    public async Task ThePageIsReusedForFifteenSeconds() {
        FakeMetricsSource source = new(_ => Answer());
        TestClock clock = new() { Now = Now };
        MetricsDashboard dashboard = new(source, Options, clock, NullLogger<MetricsDashboard>.Instance);

        await dashboard.GetAsync(CancellationToken.None);
        int afterFirst = source.Calls;
        await dashboard.GetAsync(CancellationToken.None);
        clock.Now = Now.AddSeconds(14);
        await dashboard.GetAsync(CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(afterFirst), "a refresh inside the window must not requery");

        clock.Now = Now.AddSeconds(16);
        await dashboard.GetAsync(CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(afterFirst * 2));
    }
}
