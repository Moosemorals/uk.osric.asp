// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Globalization;
using uk.osric.asp.Core.Charting;

[TestFixture]
public sealed class LineChartTests {

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-01-15T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly ChartSize Size = new(400, 200);

    private static readonly ChartAxis TimeAxis =
        new("time", [new ChartTick(0, "12:00"), new ChartTick(600, "12:10")]);

    private static readonly ChartAxis CountAxis =
        new("requests/s", [new ChartTick(0, "0"), new ChartTick(1, "1")]);

    private static readonly ChartFrame Frame = new("test", "Requests", "requests/s", "No data in this window.", TimeZoneInfo.Utc);

    private static ChartSeries Series(string label, params (int Seconds, double? Value)[] points) =>
        new(label, [.. points.Select(point => new ChartPoint(Start.AddSeconds(point.Seconds), point.Value))]);

    private static string Render(params ChartSeries[] series) =>
        LineChart.Render(series, TimeAxis, CountAxis, Size, Frame);

    [Test]
    public void TheDrawingCarriesItsOwnTitleAndLegend() {
        string svg = Render(Series("ok", (0, 0), (600, 1)));

        Assert.That(svg, Does.Contain("<title>Requests</title>"));
        Assert.That(svg, Does.Contain("<desc>requests/s over time</desc>"));
        Assert.That(svg, Does.Contain("class=\"chart-title\""));
        Assert.That(svg, Does.Contain(">Requests</text>"), "the visible title sits inside the drawing");
        Assert.That(svg, Does.Contain("class=\"chart-legend-label\""));
    }

    [Test]
    public void ASecondSeriesIsDashedAsWellAsAColour() {
        string svg = Render(Series("ok", (0, 0), (600, 1)), Series("error", (0, 1), (600, 0)));

        Assert.That(svg, Does.Contain("stroke=\"#0072B2\""), "the first palette colour is on the line");
        Assert.That(svg, Does.Contain("stroke=\"#D55E00\""), "the second is a different colour");
        Assert.That(svg, Does.Contain("stroke-dasharray=\"6 4\""), "and a different dash");
    }

    [Test]
    public void EverySeriesIsAnAnchorThatNamesItself() {
        string svg = Render(Series("ok", (0, 0), (600, 1)));

        Assert.That(svg, Does.Contain("<a class=\"series-link\" id=\"test-s1\" href=\"#test-s1\" tabindex=\"-1\">"));
        Assert.That(svg, Does.Contain("<title>ok</title>"), "the anchor names the series on hover");
    }

    [Test]
    public void TheHitStrokeIsDrawnOnlyWhereAClickMustChoose() {
        Assert.That(Render(Series("one", (0, 0), (600, 1))), Does.Not.Contain("class=\"series-hit\""));

        string two = Render(Series("one", (0, 0), (600, 1)), Series("two", (0, 1), (600, 0)));
        Assert.That(two, Does.Contain("class=\"series-hit\""));
    }

    [Test]
    public void TheEndOfEveryLineIsNamed() {
        string svg = Render(Series("ok", (0, 0), (600, 1)));

        Assert.That(svg, Does.Contain("class=\"series-label\""));
        Assert.That(svg, Does.Contain(">ok</text>"));
    }

    [Test]
    public void AGapSplitsTheLineIntoRuns() {
        string svg = Render(Series("gap", (0, 0), (300, null), (600, 1)));

        Assert.That(svg, Does.Not.Contain("<polyline class=\"series"), "the gap is not bridged");
        Assert.That(svg.Split("<circle").Length - 1, Is.EqualTo(2), "each side of the gap is its own run");
    }

    [Test]
    public void AnEmptySeriesDrawsTheEmptyState() {
        string svg = Render(Series("empty"));

        Assert.That(svg, Does.Contain("class=\"chart-empty\""));
        Assert.That(svg, Does.Contain("No data in this window."));
        Assert.That(svg, Does.Not.Contain("<polyline"));
    }

    [Test]
    public void EveryLabelIsEscaped() {
        string svg = Render(Series("<script>", (0, 0), (600, 1)));

        Assert.That(svg, Does.Contain("&lt;script&gt;"));
        Assert.That(svg, Does.Not.Contain("<script>"));
    }

    [Test]
    public void ASizeWithNoRoomForThePlotIsRefused() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LineChart.Render([Series("small", (0, 0))], TimeAxis, CountAxis, new ChartSize(40, 200), Frame));
    }

    [Test]
    public void ANullSeriesListIsRefused() {
        Assert.Throws<ArgumentNullException>(
            () => LineChart.Render(null!, TimeAxis, CountAxis, Size, Frame));
    }

    [Test]
    public void AnAxisWithoutTicksStillDrawsTheSeries() {
        string svg = LineChart.Render(
            [Series("points", (0, 2), (600, 4))],
            TimeAxis,
            new ChartAxis("count", []),
            Size,
            Frame);

        Assert.That(svg, Does.Contain("<polyline class=\"series series-1\""));
        Assert.That(svg, Does.Contain("stroke=\"#0072B2\""));
    }

    [Test]
    public void AnUnnamedChartKeepsTheFramesTitle() {
        string svg = Render(Series("", (0, 0), (600, 1)));

        Assert.That(svg, Does.Contain("<title>Requests</title>"));
    }
}
