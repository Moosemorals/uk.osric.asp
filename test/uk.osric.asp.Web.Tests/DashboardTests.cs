// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class MetricsDashboardTests {

    private static readonly MetricsOptions Options = new() {
        PrometheusUrl = new Uri("http://prometheus:9090"),
        TimeZone = TimeZoneInfo.Utc,
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-02T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static QueryResult Answer(string label = "ok") => new MetricsMatrix([
        new ChartSeries(label, [
            new ChartPoint(Now.AddMinutes(-30), 1),
            new ChartPoint(Now, 2),
        ]),
    ]);

    [Test]
    public void NoQueryUsesAnOpenTelemetryTagNameAsALabel() {
        // PromQL label names cannot contain dots: the exporter writes the tag page.path as page_path.
        Assert.That(Dashboard.Queries, Is.All.Not.Contain("page.path"));
        Assert.That(Dashboard.Queries, Has.Some.Contain("page_path"));
    }

    [Test]
    public async Task RequestsByPageIsTheFirstPanel() {
        Dashboard dashboard = new(
            new FakeMetricsSource(_ => Answer()),
            Options,
            new TestClock { Now = Now },
            NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        Assert.That(page.Panels[0].Title, Is.EqualTo("Requests by page (per second)"));
        Assert.That(page.Panels[1].Title, Is.EqualTo("Pages slower than 0.5 s (per second)"));
    }

    [Test]
    public async Task AnAnonymousSeriesIsNamedAfterItsQuery() {
        Dashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("", [new ChartPoint(Now.AddMinutes(-1), 1)]),
            ])),
            Options,
            new TestClock { Now = Now },
            NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        StatusPanel duration = page.Panels.Single(panel => panel.Title.StartsWith("Render duration p50/p95", StringComparison.Ordinal));
        Assert.That(duration.Labels, Is.EqualTo((string[])["p50", "p95"]));
    }

    [Test]
    public async Task ASubSecondDurationAxisIsDrawnInMilliseconds() {
        Dashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("", [
                    new ChartPoint(Now.AddMinutes(-30), 0.0002),
                    new ChartPoint(Now, 0.0004),
                ]),
            ])),
            Options,
            new TestClock { Now = Now },
            NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        StatusPanel duration = page.Panels.Single(panel => panel.Title.StartsWith("Render duration p50/p95", StringComparison.Ordinal));
        Assert.That(duration.Title, Is.EqualTo("Render duration p50/p95 (milliseconds)"));
        Assert.That(duration.Svg, Does.Contain(">milliseconds<"));
        Assert.That(duration.Svg, Does.Not.Contain(">seconds<"));

        StatusPanel rate = page.Panels.Single(panel => panel.Title.StartsWith("Cache lookups by result", StringComparison.Ordinal));
        Assert.That(rate.Title, Is.EqualTo("Cache lookups by result (per second)"));
        Assert.That(rate.Svg, Does.Contain(">per second<"));
    }

    [Test]
    public async Task ABytesAxisIsDrawnInMegabytes() {
        Dashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("", [
                    new ChartPoint(Now.AddMinutes(-30), 166_000_000),
                    new ChartPoint(Now, 180_000_000),
                ]),
            ])),
            Options,
            new TestClock { Now = Now },
            NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        StatusPanel memory = page.Panels.Single(panel => panel.Title.StartsWith("Process memory", StringComparison.Ordinal));
        Assert.That(memory.Title, Is.EqualTo("Process memory (megabytes)"));
        Assert.That(memory.Svg, Does.Contain(">megabytes<"));
    }

    [Test]
    public async Task EveryPanelIsDrawnWhenTheSourceAnswers() {
        Dashboard dashboard = new(new FakeMetricsSource(_ => Answer()), Options, new TestClock { Now = Now }, NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        Assert.That(page.Banner, Is.Null);
        Assert.That(page.Panels, Has.Count.EqualTo(15));
        Assert.That(page.Panels.Select(panel => panel.Svg), Is.All.Not.Null);
        Assert.That(page.Panels[0].Svg, Does.Contain("<svg class=\"chart\""));
        Assert.That(page.Panels[^1].Title, Is.EqualTo("Render duration buckets (renders/s)"));
        Assert.That(page.Panels[^1].Svg, Does.Contain("<svg class=\"heatmap\""));
    }

    [Test]
    public async Task ARemoteFailureBecomesABannerInsteadOfCharts() {
        Dashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsUnavailable("Prometheus is unreachable.")),
            Options,
            new TestClock { Now = Now },
            NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        Assert.That(page.Banner, Is.EqualTo("Prometheus is unreachable."));
        Assert.That(page.Panels, Is.Empty);
    }

    [Test]
    public async Task OneFailedPanelLeavesTheOthers() {
        Dashboard dashboard = new(
            new FakeMetricsSource(query => query.Contains("page_cache_lookups_total", StringComparison.Ordinal)
                ? new MetricsUnavailable("cache query failed")
                : Answer()),
            Options,
            new TestClock { Now = Now },
            NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        Assert.That(page.Banner, Is.EqualTo("cache query failed"));
        Assert.That(page.Panels, Has.Count.EqualTo(14));
        Assert.That(page.Panels.Any(panel => panel.Title.StartsWith("Cache lookups by result", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public async Task AnEmptyAnswerIsReportedAsNoData() {
        Dashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsMatrix([])),
            Options,
            new TestClock { Now = Now },
            NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        Assert.That(page.Panels, Has.Count.EqualTo(15));
        Assert.That(page.Panels.Select(panel => panel.Svg), Is.All.Null);
    }

    [Test]
    public async Task ThePageIsReusedForFifteenSeconds() {
        FakeMetricsSource source = new(_ => Answer());
        TestClock clock = new() { Now = Now };
        Dashboard dashboard = new(source, Options, clock, NullLogger<Dashboard>.Instance);

        await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);
        int afterFirst = source.Calls;
        await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);
        clock.Now = Now.AddSeconds(14);
        await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(afterFirst), "a refresh inside the window must not requery");

        clock.Now = Now.AddSeconds(16);
        await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(afterFirst * 2));
    }

    [Test]
    public async Task TheSelectedDurationSetsTheQueryWindow() {
        FakeMetricsSource source = new(_ => Answer());
        Dashboard dashboard = new(source, Options, new TestClock { Now = Now }, NullLogger<Dashboard>.Instance);

        await dashboard.GetAsync(StatusDuration.FromToken("7d"), CancellationToken.None);

        Assert.That(source.From, Is.EqualTo(Now.AddDays(-7)));
        Assert.That(source.To, Is.EqualTo(Now));
        Assert.That(source.Step, Is.EqualTo(StatusDuration.FromToken("7d").Step));
    }

    [Test]
    public async Task ALongerWindowIsDrawnWider() {
        Dashboard dashboard = new(new FakeMetricsSource(_ => Answer()), Options, new TestClock { Now = Now }, NullLogger<Dashboard>.Instance);

        StatusPage page = await dashboard.GetAsync(StatusDuration.FromToken("30d"), CancellationToken.None);

        Assert.That(page.Panels[0].Svg, Does.Contain("viewBox=\"0 0 1200 200\""));
        Assert.That(page.Panels[^1].Svg, Does.Contain("viewBox=\"0 0 1200 320\""));
    }

    [Test]
    public async Task EachWindowIsCachedSeparately() {
        FakeMetricsSource source = new(_ => Answer());
        TestClock clock = new() { Now = Now };
        Dashboard dashboard = new(source, Options, clock, NullLogger<Dashboard>.Instance);
        StatusDuration sevenDays = StatusDuration.FromToken("7d");

        await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);
        int afterFirst = source.Calls;
        await dashboard.GetAsync(sevenDays, CancellationToken.None);
        Assert.That(source.Calls, Is.GreaterThan(afterFirst), "a different window must be built");

        int afterSecond = source.Calls;
        await dashboard.GetAsync(StatusDuration.Default, CancellationToken.None);
        await dashboard.GetAsync(sevenDays, CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(afterSecond), "each window keeps its own cached page");
    }
}
