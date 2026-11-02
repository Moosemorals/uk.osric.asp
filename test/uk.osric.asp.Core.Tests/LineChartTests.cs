// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using System.Globalization;

[TestFixture]
public sealed class LineChartTests {

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-01-15T12:00:00Z", CultureInfo.InvariantCulture);

    // 60 × 50 leaves a 10 × 20 plot once the label margins are taken, so every coordinate is exact.
    private static readonly ChartSize Size = new(60, 50);

    private static readonly ChartAxis TimeAxis =
        new("time", [new ChartTick(0, "12:00"), new ChartTick(10, "12:10")]);

    private static readonly ChartAxis CountAxis =
        new("count", [new ChartTick(0, "0"), new ChartTick(1, "1")]);

    private static ChartSeries Series(string label, params (int Seconds, double? Value)[] points) =>
        new(label, [.. points.Select(point => new ChartPoint(Start.AddSeconds(point.Seconds), point.Value))]);

    [Test]
    public void OneSeriesRendersExactMarkup() {
        string svg = LineChart.Render([Series("Requests", (0, 0), (10, 1))], TimeAxis, CountAxis, Size);

        string expected = string.Join('\n', [
            "<svg class=\"chart\" viewBox=\"0 0 60 50\" role=\"img\">",
            "<title>Requests</title>",
            "<desc>count over time</desc>",
            "<line class=\"chart-grid\" x1=\"40\" y1=\"30\" x2=\"50\" y2=\"30\"/>",
            "<text class=\"chart-label\" x=\"36\" y=\"34\" text-anchor=\"end\">0</text>",
            "<line class=\"chart-grid\" x1=\"40\" y1=\"10\" x2=\"50\" y2=\"10\"/>",
            "<text class=\"chart-label\" x=\"36\" y=\"14\" text-anchor=\"end\">1</text>",
            "<line class=\"chart-grid\" x1=\"40\" y1=\"10\" x2=\"40\" y2=\"30\"/>",
            "<text class=\"chart-label\" x=\"40\" y=\"44\" text-anchor=\"middle\">12:00</text>",
            "<line class=\"chart-grid\" x1=\"50\" y1=\"10\" x2=\"50\" y2=\"30\"/>",
            "<text class=\"chart-label\" x=\"50\" y=\"44\" text-anchor=\"middle\">12:10</text>",
            "<polyline class=\"series series-1\" points=\"40,30 50,10\"/>",
            "</svg>",
            "",
        ]);

        Assert.That(svg, Is.EqualTo(expected));
    }

    [Test]
    public void SeveralSeriesCycleThroughThePalette() {
        string svg = LineChart.Render(
            [Series("first", (0, 0), (10, 1)), Series("second", (0, 0), (10, 1))],
            TimeAxis,
            CountAxis,
            Size);

        Assert.That(svg, Does.Contain("series series-1"));
        Assert.That(svg, Does.Contain("series series-2"));
        Assert.That(svg, Does.Contain("<title>first, second</title>"));
    }

    [Test]
    public void AGapSplitsTheLineIntoRuns() {
        string svg = LineChart.Render([Series("gap", (0, 0), (5, null), (10, 1))], TimeAxis, CountAxis, Size);

        Assert.That(svg, Does.Not.Contain("<polyline"));
        Assert.That(svg.Split("<circle").Length - 1, Is.EqualTo(2), "each side of the gap is its own run");
    }

    [Test]
    public void AnEmptySeriesDrawsNothing() {
        string svg = LineChart.Render([Series("empty")], TimeAxis, CountAxis, Size);

        Assert.That(svg, Does.Not.Contain("<polyline"));
        Assert.That(svg, Does.Not.Contain("<circle"));
        Assert.That(svg, Does.Contain("<title>empty</title>"));
    }

    [Test]
    public void AFlatSeriesKeepsANonZeroAxis() {
        ChartAxis flat = new("count", [new ChartTick(4.5, "4.5"), new ChartTick(5.5, "5.5")]);

        string svg = LineChart.Render([Series("flat", (0, 5), (10, 5))], TimeAxis, flat, Size);

        Assert.That(svg, Does.Contain("points=\"40,20 50,20\""));
    }

    [Test]
    public void ASinglePointIsDrawnAsACircle() {
        string svg = LineChart.Render([Series("one", (0, 0))], TimeAxis, CountAxis, Size);

        Assert.That(svg, Does.Contain("<circle class=\"series series-1\" cx=\"40\" cy=\"30\" r=\"2\"/>"));
    }

    [Test]
    public void EveryLabelIsEscaped() {
        string svg = LineChart.Render(
            [Series("<script>", (0, 0), (10, 1))],
            new ChartAxis("time", [new ChartTick(0, "a & b")]),
            CountAxis,
            Size);

        Assert.That(svg, Does.Contain("<title>&lt;script&gt;</title>"));
        Assert.That(svg, Does.Contain("a &amp; b"));
        Assert.That(svg, Does.Not.Contain("<script>"));
    }

    [Test]
    public void ASizeWithNoRoomForThePlotIsRefused() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LineChart.Render([Series("small", (0, 0))], TimeAxis, CountAxis, new ChartSize(40, 50)));
    }

    [Test]
    public void ANullSeriesListIsRefused() {
        Assert.Throws<ArgumentNullException>(
            () => LineChart.Render(null!, TimeAxis, CountAxis, Size));
    }
}
