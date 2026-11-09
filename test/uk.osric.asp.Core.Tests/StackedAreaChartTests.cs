// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Globalization;
using uk.osric.asp.Core.Charting;

[TestFixture]
public sealed class StackedAreaChartTests {

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-11-02T05:00:00Z", CultureInfo.InvariantCulture);

    private static readonly ChartSize Size = new(400, 260);

    private static readonly ChartAxis TimeAxis =
        new("time", [new ChartTick(0, "05:00"), new ChartTick(60, "05:01")]);

    private static readonly ChartAxis CountAxis = new("per second", []);

    private static readonly ChartFrame Frame =
        new("test", "Lookups by result", "per second", "No data in this window.", TimeZoneInfo.Utc);

    private static ChartSeries Series(string label, params (int Seconds, double? Value)[] points) =>
        new(label, [.. points.Select(point => new ChartPoint(Start.AddSeconds(point.Seconds), point.Value))]);

    private static string Render(params ChartSeries[] series) =>
        StackedAreaChart.Render(series, TimeAxis, CountAxis, Size, Frame);

    [Test]
    public void TheBandsAreStackedInLabelOrder() {
        string svg = Render(Series("miss", (0, 1), (60, 1)), Series("hit", (0, 2), (60, 2)));

        int first = svg.IndexOf("<title>hit</title>", StringComparison.Ordinal);
        int second = svg.IndexOf("<title>miss</title>", StringComparison.Ordinal);

        Assert.That(first, Is.GreaterThanOrEqualTo(0));
        Assert.That(second, Is.GreaterThan(first), "hit sorts before miss, whatever order the caller used");
    }

    [Test]
    public void EveryBandIsAnAnchorThatNamesItself() {
        string svg = Render(Series("hit", (0, 1), (60, 1)));

        Assert.That(svg, Does.Contain("<a class=\"series-link series-1\" id=\"test-s1\" href=\"#test-s1\" tabindex=\"-1\">"));
        Assert.That(svg, Does.Contain("class=\"chart-legend-label\""), "the legend entry lives in the band's anchor");
        Assert.That(svg, Does.Contain("<path class=\"series area series-1\""));
    }

    [Test]
    public void TheLegendNamesEveryBand() {
        string svg = Render(Series("hit", (0, 1), (60, 1)), Series("miss", (0, 1), (60, 1)));

        Assert.That(svg, Does.Contain("<title>Lookups by result</title>"));
        Assert.That(svg, Does.Contain("class=\"chart-legend-label\""));
        Assert.That(svg, Does.Contain(">hit</text>").And.Contain(">miss</text>"));
    }

    [Test]
    public void AMissingSampleIsZeroRatherThanAGap() {
        // The second band is absent from the first column; if that were a gap the axis would stop at 1.
        string svg = Render(Series("hit", (0, 1), (60, 1)), Series("miss", (60, 2)));

        Assert.That(svg, Does.Contain(">3</text>"), "the axis reaches the sum of both bands in the second column");
    }

    [Test]
    public void TheAxisIsTheSumOfTheBands() {
        string svg = Render(Series("hit", (0, 1), (60, 1)), Series("miss", (0, 2), (60, 2)), Series("stale", (0, 3), (60, 3)));

        Assert.That(svg, Does.Contain(">6</text>"), "1 + 2 + 3 is the top of the stack");
    }

    [Test]
    public void ANegativeValueIsRefused() {
        Assert.Throws<ArgumentException>(() => Render(Series("hit", (0, -1), (60, 1))));
    }

    [Test]
    public void AnEmptyChartDrawsTheEmptyState() {
        string svg = Render();

        Assert.That(svg, Does.Contain("class=\"chart-empty\""));
        Assert.That(svg, Does.Contain("No data in this window."));
        Assert.That(svg, Does.Not.Contain("<path class=\"series"));
    }

    [Test]
    public void LabelsAreEscaped() {
        string svg = Render(Series("<b>", (0, 1), (60, 1)));

        Assert.That(svg, Does.Contain("&lt;b&gt;"));
        Assert.That(svg, Does.Not.Contain("<b>"));
    }

    [Test]
    public void ASizeWithNoRoomForThePlotIsRefused() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StackedAreaChart.Render([Series("hit", (0, 0))], TimeAxis, CountAxis, new ChartSize(40, 260), Frame));
    }
}
