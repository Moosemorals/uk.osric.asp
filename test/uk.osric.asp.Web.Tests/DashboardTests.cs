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
        Assert.That(page.Panels[0].Svg, Does.Contain(
            "class=\"chart-title-link\" href=\"/status/chart?panel=page.render.duration&amp;start=2026-11-02T06%3A00%3A00Z&amp;duration=60\""),
            "the drawing's title links to the same chart on its own");
    }

    [Test]
    public async Task AnEmptyAnswerStillDrawsEveryPanel() {
        Dashboard dashboard = New(new FakeMetricsSource(_ => new MetricsMatrix([])), new TestClock { Now = Now });

        StatusPage page = await dashboard.GetAsync(ChartWindow.At(Now, TimeSpan.FromHours(1)), CancellationToken.None);

        Assert.That(page.Panels, Has.Count.EqualTo(StatusCatalogue.Panels.Count));
        Assert.That(page.Panels.Select(panel => panel.Svg), Is.All.Contain(">No data</text>"));
    }

    [Test]
    public async Task EveryPanelIsDrawnAtTheSameSize() {
        Dashboard dashboard = New(new FakeMetricsSource(_ => Answer()), new TestClock { Now = Now });

        StatusPage page = await dashboard.GetAsync(ChartWindow.At(Now, TimeSpan.FromHours(1)), CancellationToken.None);

        string[] viewBoxes = [.. page.Panels.Select(panel => {
            (double width, double height) = ViewBox(panel.Svg);
            return width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture);
        })];

        Assert.That(viewBoxes.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(1), "the grid only lines up at one size");
    }

    [Test]
    public async Task AHeatmapNamesWhatItsCellsCount() {
        Dashboard dashboard = New(new FakeMetricsSource(_ => Answer()), new TestClock { Now = Now });

        StatusPage page = await dashboard.GetAsync(ChartWindow.At(Now, TimeSpan.FromHours(1)), CancellationToken.None);

        string svg = page.Panels.Single(panel => panel.Panel.Id == "page.image.size").Svg;

        Assert.That(svg, Does.Contain("compiles/s"), "an assembly size histogram counts the compiles that emitted one");
        Assert.That(svg, Does.Not.Contain("per second"));
    }

    [Test]
    public async Task ABytePanelDrawsItsLevelInsideThePlot() {
        // A gauge answers in bytes and its axis says so; only the tick text is shortened.
        Dashboard dashboard = New(
            new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("working set", [
                    new ChartPoint(Now.AddMinutes(-30), 180_000_000),
                    new ChartPoint(Now, 200_000_000),
                ]),
            ])),
            new TestClock { Now = Now });

        StatusPage page = await dashboard.GetAsync(ChartWindow.At(Now, TimeSpan.FromHours(1)), CancellationToken.None);

        string svg = page.Panels.Single(panel => panel.Panel.Id == "host.memory").Svg;
        double height = ViewBox(svg).Height;
        double[] ys = [.. PolylinePoints(svg).Select(point => point.Y)];

        Assert.That(svg, Does.Contain(">bytes</text>"), "the axis names the recorded unit");
        Assert.That(svg, Does.Contain(">200M</text>"), "and its tick text is shortened rather than rescaled");
        Assert.That(ys, Is.Not.Empty, "the line is drawn");
        Assert.That(ys, Is.All.InRange(0d, height), "every sample lands inside the drawing");
    }

    [Test]
    public async Task AVolumeCapacityAxisLabelsGigabytesUnderABytesTitle() {
        Dashboard dashboard = New(
            new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("capacity", [
                    new ChartPoint(Now.AddMinutes(-30), 73_000_000_000),
                    new ChartPoint(Now, 90_000_000_000),
                ]),
            ])),
            new TestClock { Now = Now });

        StatusPage page = await dashboard.GetAsync(ChartWindow.At(Now, TimeSpan.FromHours(1)), CancellationToken.None);

        string svg = page.Panels.Single(panel => panel.Panel.Id == "page.storage.volume.capacity").Svg;

        Assert.That(svg, Does.Contain(">bytes</text>"), "the axis says the samples are bytes");
        Assert.That(svg, Does.Contain(">70G</text>"), "a tick reads as a short byte figure");
        Assert.That(svg, Does.Contain(">90G</text>"));
        Assert.That(svg, Does.Not.Contain("megabytes"));
    }

    private static (double Width, double Height) ViewBox(string svg) {
        const string marker = "viewBox=\"0 0 ";
        int start = svg.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        Assert.That(start, Is.GreaterThanOrEqualTo(marker.Length), "the drawing has no viewBox");

        string[] parts = svg[start..svg.IndexOf('"', start)].Split(' ');
        return (
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static IEnumerable<(double X, double Y)> PolylinePoints(string svg) {
        const string marker = "<polyline class=\"series series-1\" points=\"";
        int start = svg.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        Assert.That(start, Is.GreaterThanOrEqualTo(marker.Length), "the series is not drawn");

        return svg[start..svg.IndexOf('"', start)]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => {
                string[] parts = pair.Split(',');
                return (
                    double.Parse(parts[0], CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture));
            });
    }

    private static Dashboard New(IMetricsSource source, TimeProvider clock) =>
        new(new ChartRenderer(source, Options), clock, NullLogger<Dashboard>.Instance);
}
