// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Globalization;
using uk.osric.asp.Core.Charting;

/// <summary>Pins what the three renderers must agree about, so they cannot drift apart.</summary>
/// <remarks>
/// The renderers draw different things, but they share one band, one plot rectangle and one idea of
/// what an empty chart still shows. Each of these assertions is about the drawing as a whole, so it
/// belongs here rather than beside the renderer that happens to satisfy it today.
/// </remarks>
[TestFixture]
public sealed class ChartConsistencyTests {

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-11-02T05:00:00Z", CultureInfo.InvariantCulture);

    private static readonly ChartSize Size = new(400, 320);

    private static readonly ChartAxis TimeAxis =
        new("time", [new ChartTick(0, "05:00"), new ChartTick(60, "05:01")]);

    private static readonly ChartFrame Frame =
        new("test", "Render duration", "seconds", "No data", TimeZoneInfo.Utc);

    private static ChartSeries Series(string label, params (int Seconds, double? Value)[] points) =>
        new(label, [.. points.Select(point => new ChartPoint(Start.AddSeconds(point.Seconds), point.Value))]);

    [Test]
    public void TheTitleAndTheLegendStartWhereThePlotDoes() {
        foreach ((string name, string svg) in DrawingsWithData()) {
            double plot = ElementAttribute(svg, "chart-frame", "x");

            Assert.That(ElementAttribute(svg, "chart-title", "x"), Is.EqualTo(plot), $"{name}'s title lines up with the axis");
            Assert.That(LegendLeft(svg), Is.EqualTo(plot), $"{name}'s legend lines up with the axis");
        }
    }

    [Test]
    public void AnEmptyChartStillDrawsItsHorizontalLine() {
        foreach ((string name, string svg) in DrawingsWithoutData()) {
            Assert.That(svg, Does.Contain("class=\"chart-grid\""), $"{name} keeps its horizontal lines");
        }
    }

    [Test]
    public void AnEmptyChartStillDrawsItsFrameAndItsTimeAxis() {
        foreach ((string name, string svg) in DrawingsWithoutData()) {
            Assert.That(svg, Does.Contain("class=\"chart-frame\""), $"{name} keeps its frame");
            Assert.That(svg, Does.Contain("class=\"chart-empty\""), $"{name} says why it is empty");
            Assert.That(svg, Does.Contain(">05:00</text>"), $"{name} keeps its time labels");
        }
    }

    [Test]
    public void AnEmptyChartKeepsItsTimeLabelsInsideThePlot() {
        foreach ((string name, string svg) in DrawingsWithoutData()) {
            double left = ElementAttribute(svg, "chart-frame", "x");
            double right = left + ElementAttribute(svg, "chart-frame", "width");

            Assert.That(
                TextX(svg, "05:01"),
                Is.EqualTo(right).Within(0.01),
                $"{name} draws its last tick at the plot's right edge");
        }
    }

    [Test]
    public void AnEmptyBucketMapKeepsTheBucketsItWasGiven() {
        string svg = Heatmap.Render(
            [new HeatmapRow("0.5", [new ChartPoint(Start, 0), new ChartPoint(Start.AddSeconds(60), 0)])],
            TimeAxis, Size, Frame, "renders/s");

        Assert.That(svg, Does.Contain("class=\"chart-empty\""));
        Assert.That(svg, Does.Contain(">0.5</text>"), "the bucket is still named");
        Assert.That(svg, Does.Contain("class=\"chart-grid\""), "and its row still has an edge");
    }

    private static IEnumerable<(string Name, string Svg)> DrawingsWithData() => [
        ("line", LineChart.Render(
            [Series("ok", (0, 1), (60, 2))], TimeAxis, new ChartAxis("count", [new ChartTick(0, "0"), new ChartTick(2, "2")]), Size, Frame)),
        ("stacked", StackedAreaChart.Render(
            [Series("hit", (0, 1), (60, 2))], TimeAxis, new ChartAxis("per second", []), Size, Frame)),
        ("heatmap", Heatmap.Render(
            [new HeatmapRow("0.5", [new ChartPoint(Start, 1), new ChartPoint(Start.AddSeconds(60), 2)])],
            TimeAxis, Size, Frame, "renders/s")),
    ];

    private static IEnumerable<(string Name, string Svg)> DrawingsWithoutData() => [
        ("line", LineChart.Render(
            [Series("ok", (0, null), (60, null))], TimeAxis, new ChartAxis("count", [new ChartTick(0, "0"), new ChartTick(2, "2")]), Size, Frame)),
        ("stacked", StackedAreaChart.Render([], TimeAxis, new ChartAxis("per second", []), Size, Frame)),
        ("heatmap", Heatmap.Render(
            [new HeatmapRow("0.5", [new ChartPoint(Start, 0), new ChartPoint(Start.AddSeconds(60), 0)])],
            TimeAxis, Size, Frame, "renders/s")),
    ];

    // A line chart and a stacked area start their legend with the dash sample; a bucket map starts it
    // with the shade scale's zero. Either way the band begins at the plot's left edge.
    private static double LegendLeft(string svg) =>
        svg.Contains("class=\"chart-legend-key", StringComparison.Ordinal)
            ? ElementAttribute(svg, "chart-legend-key", "x1")
            : TextX(svg, "0");

    private static double ElementAttribute(string svg, string className, string attribute) {
        int at = svg.IndexOf("class=\"" + className, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $".{className} is not in the markup");

        int tagStart = svg.LastIndexOf('<', at);
        int tagEnd = svg.IndexOf('>', at);

        return Attribute(svg[tagStart..tagEnd], attribute);
    }

    private static double TextX(string svg, string label) {
        int at = svg.IndexOf(">" + label + "</text>", StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"the label {label} is not in the markup");

        int tagStart = svg.LastIndexOf("<text", at, StringComparison.Ordinal);

        return Attribute(svg[tagStart..at], "x");
    }

    private static double Attribute(string markup, string name) {
        string prefix = name + "=\"";
        int start = markup.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        Assert.That(start, Is.GreaterThanOrEqualTo(prefix.Length), $"{name} is not in the markup");

        int close = markup.IndexOf('"', start);
        return double.Parse(markup[start..close], CultureInfo.InvariantCulture);
    }
}
