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

    private static IEnumerable<(double X, double Y)> PolylinePoints(string svg) {
        const string marker = "<polyline class=\"series series-1\" points=\"";
        int start = svg.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        Assert.That(start, Is.GreaterThanOrEqualTo(marker.Length), "the first series is not drawn");
        string points = svg[start..svg.IndexOf('"', start)];

        return points.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(pair => {
            string[] parts = pair.Split(',');
            return (
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture));
        });
    }

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

        Assert.That(svg, Does.Contain("<a class=\"series-link series-1\" id=\"test-s1\" href=\"#test-s1\" tabindex=\"-1\">"));
        Assert.That(svg, Does.Contain("<title>ok</title>"), "the anchor names the series on hover");

        int open = svg.IndexOf("<a class=\"series-link series-1\"", StringComparison.Ordinal);
        string anchor = svg[open..svg.IndexOf("</a>", open, StringComparison.Ordinal)];

        Assert.That(anchor, Does.Contain("chart-legend-label"), "the legend entry lives in the series' anchor");
        Assert.That(anchor, Does.Contain("<polyline class=\"series series-1\""), "and so does the line");
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
    public void TheTitleLinksToTheChartOnItsOwnOnlyWhenItIsGivenSomewhereToGo() {
        ChartFrame linked = Frame with { TitleHref = "/status/chart?panel=test" };

        string svg = LineChart.Render([Series("ok", (0, 0), (600, 1))], TimeAxis, CountAxis, Size, linked);

        Assert.That(svg, Does.Contain("<a class=\"chart-title-link\" href=\"/status/chart?panel=test\">"));
        Assert.That(svg, Does.Contain("</a>\n<text class=\"chart-axis-title\""));
        Assert.That(Render(Series("ok", (0, 0), (600, 1))), Does.Not.Contain("chart-title-link"));
    }

    [Test]
    public void ARescaledAxisDrawsItsSamplesInTheUnitItsLabelsName() {
        ChartAxis megabytes = new("megabytes", [new ChartTick(180, "180"), new ChartTick(200, "200")], Divisor: 1_000_000);

        string svg = LineChart.Render(
            [Series("memory", (0, 180_000_000), (600, 200_000_000))],
            TimeAxis,
            megabytes,
            Size,
            Frame);

        double[] ys = [.. PolylinePoints(svg).Select(point => point.Y)];

        Assert.That(ys, Is.All.InRange(0d, Size.Height), "a sample in bytes belongs on the axis its labels name");
        Assert.That(ys[0], Is.GreaterThan(ys[1]), "180 megabytes sits below 200");
    }

    [Test]
    public void TheDrawnAreaIsFramed() {
        string svg = Render(Series("ok", (0, 0), (600, 1)));

        Assert.That(svg, Does.Contain("class=\"chart-frame\""));
        Assert.That(svg, Does.Contain("fill=\"none\" stroke=\"#999999\""));
    }

    [Test]
    public void TheValueAxisUsesThreeSignificantFiguresAndAnSiSuffix() {
        ChartAxis large = new("bytes", [new ChartTick(0, "0"), new ChartTick(1_048_576, "1048576")]);

        string svg = LineChart.Render([Series("big", (0, 0), (600, 1_048_576))], TimeAxis, large, Size, Frame);

        Assert.That(svg, Does.Contain(">1M</text>"));
        Assert.That(svg, Does.Not.Contain("1.048576e+06"));
    }

    [Test]
    public void AnUnnamedChartKeepsTheFramesTitle() {
        string svg = Render(Series("", (0, 0), (600, 1)));

        Assert.That(svg, Does.Contain("<title>Requests</title>"));
    }

    [Test]
    public void TheDrawingDeclaresTheSvgNamespace() {
        // Inline in a page the namespace is implied; served on its own it is not, and a browser shows
        // an element it cannot place as a tree of markup rather than as a drawing.
        Assert.That(
            Render(Series("ok", (0, 0), (600, 1))),
            Does.StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\" "));
    }

    [Test]
    public void TheTypeIsTenPerCentLargerThanTheFirstDraft() {
        string svg = Render(Series("ok", (0, 0), (600, 1)));

        Assert.That(
            svg,
            Does.Contain("class=\"chart-title\" x=\"66\" y=\"15.4\" font-size=\"13.2\""),
            "the title is twelve pixels plus a tenth, in a band that grew with it");
        Assert.That(svg, Does.Contain("\" font-size=\"12.1\""), "the axis labels are eleven pixels plus a tenth");
    }
}
