// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Globalization;
using uk.osric.asp.Core.Charting;

[TestFixture]
public sealed class HeatmapTests {

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-11-02T05:00:00Z", CultureInfo.InvariantCulture);

    private static readonly ChartSize Size = new(400, 320);

    private static readonly ChartAxis TimeAxis =
        new("time", [new ChartTick(0, "05:00"), new ChartTick(60, "05:01")]);

    private static readonly ChartFrame Frame =
        new("test", "Render duration", "seconds", "No data in this window.", TimeZoneInfo.Utc);

    private static HeatmapRow Row(string label, params double?[] values) =>
        new(label, [.. values.Select((value, index) => new ChartPoint(Start.AddMinutes(index), value))]);

    private static string Render(params HeatmapRow[] rows) =>
        Heatmap.Render(rows, TimeAxis, Size, Frame, "renders/s");

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
    public void EveryNonEmptySampleAppearsInARun() {
        string svg = Render(Row("0.1", 1, 0, (double?)null), Row("0.5", 2, 3, 4));

        Assert.That(Cells(svg), Is.EqualTo(3), "one run in the first row and two in the second");
        Assert.That(svg, Does.Contain(">0.1: 1 renders/s (05:00)<"));
    }

    [Test]
    public void NeighbouringCellsOfTheSameShadeMergeIntoOneRun() {
        string svg = Render(Row("0.5", 4, 4, 4, 0, 4));

        Assert.That(Cells(svg), Is.EqualTo(2), "a run of three and a lone cell");
        Assert.That(svg, Does.Contain("<title>0.5: 12 renders/s (05:00–05:02)</title>"));
    }

    [Test]
    public void TheBusiestRunGetsTheDarkestShade() {
        string svg = Render(Row("0.1", 1, 100));

        Assert.That(svg, Does.Contain("fill=\"#1e3a8a\""), "the darkest shade is the busiest run");
    }

    [Test]
    public void TheSmallestBucketIsDrawnAtTheBottom() {
        string svg = Render(Row("0.1", 1), Row("0.5", 1), Row("+Inf", 1));

        double smallest = LabelTop(svg, "0.1");
        double largest = LabelTop(svg, "+Inf");

        Assert.That(smallest, Is.GreaterThan(largest), "y grows downwards, so the smallest bucket has the larger y");
    }

    [Test]
    public void TheDrawingCarriesItsOwnTitleAndTheShadeLegend() {
        string svg = Render(Row("0.5", 3, 12));

        Assert.That(svg, Does.Contain("<title>Render duration</title>"));
        Assert.That(svg, Does.Contain("class=\"chart-title\""));
        Assert.That(svg, Does.Contain(">0</text>"));
        Assert.That(svg, Does.Contain(">12 renders/s</text>"));
        Assert.That(svg, Does.Contain("legend\""), "the shade swatches are labelled as the legend");
    }

    [Test]
    public void RowLabelsAreEscaped() {
        string svg = Render(Row("<b>", 1));

        Assert.That(svg, Does.Contain("&lt;b&gt;"));
        Assert.That(svg, Does.Not.Contain("<b>"));
    }

    [Test]
    public void AnEmptyHeatmapDrawsTheEmptyState() {
        string svg = Render();

        Assert.That(Cells(svg), Is.EqualTo(0));
        Assert.That(svg, Does.Contain("class=\"chart-empty\""));
        Assert.That(svg, Does.Contain("class=\"heatmap\""));
    }

    [Test]
    public void ABusyAnswerIsFoldedToTheColumnCap() {
        IReadOnlyList<HeatmapRow> rows = [Row("0.5", [.. Enumerable.Range(0, 300).Select(index => (double?)index)])];

        IReadOnlyList<HeatmapRow> folded = Heatmap.Fold(rows, Heatmap.MaxColumns);

        Assert.That(folded[0].Points, Has.Count.LessThanOrEqualTo(Heatmap.MaxColumns));
        Assert.That(folded[0].Points.Sum(point => point.Value), Is.EqualTo(Enumerable.Range(0, 300).Sum()));
    }

    [Test]
    public void TicksArePlacedByTheirSecondsFromTheFirstSample() {
        string svg = Render(Row("0.5", 1, 2));

        Assert.That(AttributeOf(svg, "05:00", "x"), Is.EqualTo(60), "the left margin");
        Assert.That(AttributeOf(svg, "05:01", "x"), Is.EqualTo(390), "the whole plot width later");
    }

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

    // Cells are the run rects, which carry no class; the shade legend's swatches do.
    private static int Cells(string svg) => svg.Split("<rect x=\"").Length - 1;
}
