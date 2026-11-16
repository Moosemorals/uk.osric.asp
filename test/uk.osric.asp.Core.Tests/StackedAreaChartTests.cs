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

    private static readonly ChartTimeAxis TimeAxis =
        new("time", Start, Start.AddSeconds(60), [new ChartTick(0, "05:00"), new ChartTick(60, "05:01")]);

    private static readonly ChartAxis CountAxis = new("per second", []);

    private static readonly ChartFrame Frame =
        new("test", "Lookups by result", "per second", "No data in this window.", TimeZoneInfo.Utc);

    private static ChartSeries Series(string label, params (int Seconds, double? Value)[] points) =>
        new(label, [.. points.Select(point => new ChartPoint(Start.AddSeconds(point.Seconds), point.Value))]);

    private static string Render(params ChartSeries[] series) =>
        StackedAreaChart.Render(series, TimeAxis, CountAxis, Size, Frame);

    private static IEnumerable<(double X, double Y)> TopPoints(string svg) {
        const string marker = "<polyline class=\"series series-1\" points=\"";
        int start = svg.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        Assert.That(start, Is.GreaterThanOrEqualTo(marker.Length), "the first band is not drawn");
        string points = svg[start..svg.IndexOf('"', start)];

        return points.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(pair => {
            string[] parts = pair.Split(',');
            return (
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture));
        });
    }

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
    public void AColumnSitsAtItsFractionOfTheWindowNotAtTheLeftEdge() {
        // Two columns a minute apart in a four-minute window: the first belongs a quarter of the way
        // across the plot, not at its left edge.
        ChartTimeAxis window = new(
            "time", Start, Start.AddSeconds(240), [new ChartTick(0, "05:00"), new ChartTick(240, "05:04")]);

        string svg = StackedAreaChart.Render([Series("hit", (60, 1), (120, 2))], window, CountAxis, Size, Frame);

        (double X, double Y)[] points = [.. TopPoints(svg)];

        Assert.That(points, Has.Length.EqualTo(2));
        Assert.That(points[0].X, Is.EqualTo(146.5).Within(0.01), "a quarter of the 322-wide plot after its 66 left edge");
        Assert.That(points[1].X, Is.EqualTo(227).Within(0.01), "and half way across");
    }

    [Test]
    public void AColumnPastTheEndOfTheWindowIsCutAtTheEdgeRatherThanDrawnAlongIt() {
        ChartTimeAxis window = new(
            "time", Start, Start.AddSeconds(240), [new ChartTick(0, "05:00"), new ChartTick(240, "05:04")]);

        string svg = StackedAreaChart.Render(
            [Series("hit", (0, 1), (120, 2), (480, 3))], window, CountAxis, Size, Frame);

        (double X, double Y)[] points = [.. TopPoints(svg)];

        Assert.That(points, Has.Length.EqualTo(3), "the column past the end is not drawn along the edge");
        Assert.That(points[^1].X, Is.EqualTo(388).Within(0.01), "the band is cut where the window ends");
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
