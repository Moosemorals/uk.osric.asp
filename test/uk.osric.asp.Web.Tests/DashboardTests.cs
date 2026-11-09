// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class DashboardTests {

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-11-02T06:00:00Z", CultureInfo.InvariantCulture);

    private static readonly MetricsOptions Options = new() {
        PrometheusUrl = new Uri("http://prometheus:9090"),
        TimeZone = TimeZoneInfo.Utc,
    };

    private static QueryResult Answer() => new MetricsMatrix([
        new ChartSeries("ok", [new ChartPoint(Now.AddMinutes(-30), 1), new ChartPoint(Now, 2)]),
    ]);

    [Test]
    public async Task ThePageIsReusedForFifteenSeconds() {
        FakeMetricsSource source = new(_ => Answer());
        TestClock clock = new() { Now = Now };
        Dashboard dashboard = New(source, clock);
        ChartWindow window = ChartWindow.At(Now, TimeSpan.FromHours(1));

        await dashboard.GetAsync(window, CancellationToken.None);
        int afterFirst = source.Calls;
        await dashboard.GetAsync(window, CancellationToken.None);
        clock.Now = Now.AddSeconds(14);
        await dashboard.GetAsync(window, CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(afterFirst), "a refresh inside the window must not requery");

        clock.Now = Now.AddSeconds(16);
        await dashboard.GetAsync(window, CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(afterFirst * 2));
    }

    [Test]
    public async Task EachWindowIsCachedSeparately() {
        FakeMetricsSource source = new(_ => Answer());
        TestClock clock = new() { Now = Now };
        Dashboard dashboard = New(source, clock);
        ChartWindow hour = ChartWindow.At(Now, TimeSpan.FromHours(1));
        ChartWindow day = ChartWindow.At(Now, TimeSpan.FromDays(1));

        await dashboard.GetAsync(hour, CancellationToken.None);
        await dashboard.GetAsync(day, CancellationToken.None);
        int afterBoth = source.Calls;

        await dashboard.GetAsync(hour, CancellationToken.None);
        await dashboard.GetAsync(day, CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(afterBoth), "each window keeps its own cached page");
    }

    [Test]
    public async Task TheCacheIsBounded() {
        FakeMetricsSource source = new(_ => Answer());
        TestClock clock = new() { Now = Now };
        Dashboard dashboard = New(source, clock);

        for (int index = 0; index < 17; index++) {
            await dashboard.GetAsync(ChartWindow.At(Now.AddHours(-index), TimeSpan.FromHours(1)), CancellationToken.None);
        }

        int afterAll = source.Calls;
        await dashboard.GetAsync(ChartWindow.At(Now, TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.That(source.Calls, Is.GreaterThan(afterAll), "the oldest window was evicted rather than kept for ever");
    }

    [Test]
    public async Task AFailedPanelBecomesABannerAndTheRestAreKept() {
        FakeMetricsSource source = new(query => query.Contains("page_cache_lookups_total", StringComparison.Ordinal)
            ? new MetricsUnavailable("cache query failed")
            : Answer());
        Dashboard dashboard = New(source, new TestClock { Now = Now });

        StatusPage page = await dashboard.GetAsync(ChartWindow.At(Now, TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.That(page.Banner, Is.EqualTo("cache query failed"));
        Assert.That(page.Panels, Has.Count.EqualTo(StatusCatalogue.Panels.Count - 1));
        Assert.That(page.Panels.Any(panel => panel.Panel.Id == "page.cache.lookups"), Is.False);
    }

    [Test]
    public async Task EveryPanelIsDrawnForAGoodAnswer() {
        Dashboard dashboard = New(new FakeMetricsSource(_ => Answer()), new TestClock { Now = Now });
        ChartWindow window = ChartWindow.At(Now, TimeSpan.FromHours(1));

        StatusPage page = await dashboard.GetAsync(window, CancellationToken.None);

        Assert.That(page.Banner, Is.Null);
        Assert.That(page.Panels, Has.Count.EqualTo(StatusCatalogue.Panels.Count));
        Assert.That(page.Panels.Select(panel => panel.Svg), Is.All.Contain("<svg"));
        Assert.That(page.Panels[0].Href, Is.EqualTo(
            "/status/chart?panel=page.render.duration&start=2026-11-02T06%3A00%3A00Z&duration=60"));
    }

    [Test]
    public async Task AnEmptyAnswerStillDrawsEveryPanel() {
        Dashboard dashboard = New(new FakeMetricsSource(_ => new MetricsMatrix([])), new TestClock { Now = Now });

        StatusPage page = await dashboard.GetAsync(ChartWindow.At(Now, TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.That(page.Panels, Has.Count.EqualTo(StatusCatalogue.Panels.Count));
        Assert.That(page.Panels.Select(panel => panel.Svg), Is.All.Contain("No data in this window."));
    }

    private static Dashboard New(IMetricsSource source, TimeProvider clock) =>
        new(new ChartRenderer(source, Options), clock, NullLogger<Dashboard>.Instance);
}
