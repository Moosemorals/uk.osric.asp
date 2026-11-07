// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using System.Globalization;
using uk.osric.asp.Core.Charting;

[TestFixture]
public sealed class HeatmapTests {

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-11-02T05:00:00Z", CultureInfo.InvariantCulture);

    private static readonly ChartSize Size = new(200, 100);

    private static ChartAxis TimeAxis =>
        new("time", [new ChartTick(0, "05:00"), new ChartTick(60, "05:01")]);

    private static HeatmapRow Row(string label, params double?[] values) =>
        new(label, [.. values.Select((value, index) => new ChartPoint(Start.AddMinutes(index), value))]);

    [Test]
    public void CumulativeBucketsBecomePerBucketCounts() {
        IReadOnlyList<HeatmapRow> rows = Heatmap.FromCumulative([
            Row("+Inf", 20, 100),
            Row("0.1", 10, 20),
            Row("0.5", 15, 40),
        ]);

        Assert.That(rows.Select(row => row.Label), Is.EqualTo((string[])["0.1", "0.5", "+Inf"]));
        Assert.That(Values(rows[0]), Is.EqualTo("10 20"));
        Assert.That(Values(rows[1]), Is.EqualTo("5 20"));
        Assert.That(Values(rows[2]), Is.EqualTo("5 60"));
    }

    [Test]
    public void ACounterResetIsClampedRatherThanNegative() {
        IReadOnlyList<HeatmapRow> rows = Heatmap.FromCumulative([Row("0.1", 10), Row("0.5", 5)]);

        Assert.That(Values(rows[1]), Is.EqualTo("0"));
    }

    [Test]
    public void AGapStaysAGap() {
        IReadOnlyList<HeatmapRow> rows = Heatmap.FromCumulative([Row("0.1", 10), Row("0.5", (double?)null)]);

        Assert.That(rows[1].Points[0].Value, Is.Null);
    }

    [Test]
    public void NonNumericBoundariesSortLast() {
        IReadOnlyList<HeatmapRow> rows = Heatmap.FromCumulative([
            Row("0.5", 1),
            Row("+Inf", 2),
            Row("0.1", 1),
        ]);

        Assert.That(rows[^1].Label, Is.EqualTo("+Inf"));
    }

    [Test]
    public void OneCellIsDrawnPerNonEmptySample() {
        string svg = Heatmap.Render([Row("0.1", 1, 0, (double?)null), Row("0.5", 2, 3, 4)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(Cells(svg), Is.EqualTo(4), "one title per cell, and none for the legend");
        Assert.That(svg, Does.Contain("<title>Duration</title>"));
    }

    [Test]
    public void TheBusiestCellGetsTheDarkestClass() {
        string svg = Heatmap.Render([Row("0.1", 1, 100)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(svg, Does.Contain("heat heat-5"));
        Assert.That(Count(svg, "heat heat-5\""), Is.EqualTo(1), "the darkest cell; the legend swatch comes later");
    }

    [Test]
    public void TheSmallestBucketIsDrawnAtTheBottom() {
        string svg = Heatmap.Render([Row("0.1", 1), Row("0.5", 1), Row("+Inf", 1)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        double smallest = LabelTop(svg, "0.1");
        double largest = LabelTop(svg, "+Inf");

        Assert.That(smallest, Is.GreaterThan(largest), "y grows downwards, so the smallest bucket has the larger y");
    }

    [Test]
    public void EveryCellNamesItsValueOnHover() {
        string svg = Heatmap.Render([Row("0.5", 3)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(svg, Does.Contain("<title>0.5 seconds: 3 renders/s</title>"));
    }

    [Test]
    public void TheLegendSpansZeroToTheBusiestCell() {
        string svg = Heatmap.Render([Row("0.5", 3, 12)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(svg, Does.Contain(">0</text>"));
        Assert.That(svg, Does.Contain(">12 renders/s</text>"));
        Assert.That(Count(svg, "legend"), Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public void RowLabelsAreEscaped() {
        string svg = Heatmap.Render([Row("<b>", 1)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(svg, Does.Contain("&lt;b&gt;"));
        Assert.That(svg, Does.Not.Contain("<b>"));
    }

    [Test]
    public void AnEmptyHeatmapDrawsNoCells() {
        string svg = Heatmap.Render([], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(Cells(svg), Is.EqualTo(0));
        Assert.That(svg, Does.Contain("<svg class=\"heatmap\""));
    }

    [Test]
    public void MissingCumulativeRowsAreRejected() {
        Assert.That(() => Heatmap.FromCumulative(null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("cumulative"));
    }

    [Test]
    public void TicksArePlacedByTheirSecondsFromTheFirstSample() {
        string svg = Heatmap.Render([Row("0.5", 1, 2)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(AttributeOf(svg, "05:00", "x"), Is.EqualTo(60), "the left margin");
        Assert.That(AttributeOf(svg, "05:01", "x"), Is.EqualTo(190), "the whole plot width later");
    }

    [Test]
    public void TheSpanFollowsTheEarliestAndLatestSamplesWhereverTheyAre() {
        HeatmapRow outOfOrder = new("0.5", [
            new ChartPoint(Start.AddMinutes(1), 1),
            new ChartPoint(Start, 1),
        ]);

        string svg = Heatmap.Render([outOfOrder], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(AttributeOf(svg, "05:00", "x"), Is.EqualTo(60));
        Assert.That(AttributeOf(svg, "05:01", "x"), Is.EqualTo(190));
    }

    [Test]
    public void TheLegendSitsUnderThePlot() {
        string svg = Heatmap.Render([Row("0.5", 3)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(Swatches(svg).Select(swatch => swatch.Y), Is.All.EqualTo(80), "100 - 24 + 4");
        Assert.That(AttributeOf(svg, "0", "y"), Is.EqualTo(90), "the swatch's baseline");
        Assert.That(AttributeOf(svg, "3 renders/s", "y"), Is.EqualTo(90));
    }

    [Test]
    public void TheLegendSwatchesStepAcrossFromTheLeftMargin() {
        string svg = Heatmap.Render([Row("0.5", 3)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(Swatches(svg).Select(swatch => swatch.X), Is.EqualTo((double[])[72, 88, 104, 120, 136]));
        Assert.That(Swatches(svg).Select(swatch => swatch.Width), Is.All.EqualTo(14));
        Assert.That(AttributeOf(svg, "3 renders/s", "x"), Is.EqualTo(154), "past the last swatch, plus its gap");
    }

    [TestCase(0.0125, "0.0125")]
    [TestCase(12.3456, "12.35")]
    public void TheLegendLabelsTheTopOfTheRange(double max, string expected) {
        string svg = Heatmap.Render([Row("0.5", max)], TimeAxis, Size, "Duration", "seconds", "renders/s");

        Assert.That(svg, Does.Contain($">{expected} renders/s</text>"));
    }

    private static IReadOnlyList<(double X, double Y, double Width)> Swatches(string svg) =>
        [.. svg.Split('<')
            .Where(element => element.Contains(" legend\"", StringComparison.Ordinal))
            .Select(element => (Attribute(element, "x"), Attribute(element, "y"), Attribute(element, "width")))];

    private static double AttributeOf(string svg, string text, string attribute) {
        string marker = ">" + text + "</text>";
        int end = svg.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), $"{text} is not in the markup");

        return Attribute(svg[..end], attribute);
    }

    private static double Attribute(string markup, string name) {
        string prefix = name + "=\"";
        int start = markup.LastIndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        Assert.That(start, Is.GreaterThanOrEqualTo(prefix.Length), $"{name} is not in the markup");

        int close = markup.IndexOf('"', start);
        return double.Parse(markup[start..close], CultureInfo.InvariantCulture);
    }

    private static double LabelTop(string svg, string label) {
        string marker = "text-anchor=\"end\">" + System.Net.WebUtility.HtmlEncode(label) + "</text>";
        int end = svg.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), $"{label} is not in the markup");

        int y = svg.LastIndexOf(" y=\"", end, StringComparison.Ordinal) + 4;
        int close = svg.IndexOf('"', y);
        return double.Parse(svg[y..close], CultureInfo.InvariantCulture);
    }

    private static string Values(HeatmapRow row) =>
        string.Join(' ', row.Points.Select(point => point.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "null"));

    // Every cell and the chart itself carry a title; the chart's is the first.
    private static int Cells(string svg) => Count(svg, "</title>") - 1;

    private static int Count(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;
}
