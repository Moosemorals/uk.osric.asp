// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using System.Globalization;

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
        string svg = Heatmap.Render([Row("0.1", 1, 0, null), Row("0.5", 2, 3, 4)], TimeAxis, Size, "Duration");

        Assert.That(Count(svg, "<rect"), Is.EqualTo(4));
        Assert.That(svg, Does.Contain("<title>Duration</title>"));
    }

    [Test]
    public void TheBusiestCellGetsTheDarkestClass() {
        string svg = Heatmap.Render([Row("0.1", 1, 100)], TimeAxis, Size, "Duration");

        Assert.That(svg, Does.Contain("heat heat-5"));
        Assert.That(Count(svg, "heat heat-5"), Is.EqualTo(1));
    }

    [Test]
    public void RowLabelsAreEscaped() {
        string svg = Heatmap.Render([Row("<b>", 1)], TimeAxis, Size, "Duration");

        Assert.That(svg, Does.Contain("&lt;b&gt;"));
        Assert.That(svg, Does.Not.Contain("<b>"));
    }

    [Test]
    public void AnEmptyHeatmapDrawsNoCells() {
        string svg = Heatmap.Render([], TimeAxis, Size, "Duration");

        Assert.That(Count(svg, "<rect"), Is.EqualTo(0));
        Assert.That(svg, Does.Contain("<svg class=\"heatmap\""));
    }

    private static string Values(HeatmapRow row) =>
        string.Join(' ', row.Points.Select(point => point.Value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "null"));

    private static int Count(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;
}
