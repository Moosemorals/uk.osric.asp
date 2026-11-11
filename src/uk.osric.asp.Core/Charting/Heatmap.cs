// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Globalization;
using System.Text;
using uk.osric.asp.Core.Assistant;

/// <summary>Turns cumulative histogram counters into a heatmap.</summary>
/// <remarks>
/// <para>
/// Prometheus exposes an explicit-bucket histogram as cumulative counters, one series per <c>le</c>
/// boundary. The count in a bucket is the difference between its boundary and the one below it, and
/// drawing the cumulative values would show every row as a superset of the row above.
/// </para>
/// <para>
/// Sixteen heatmaps on one page have to fit in a budget, so a row is drawn as runs: neighbouring cells
/// of the same shade become one <c>rect</c> with one hover title, and an answer with more columns than
/// the cap allows is folded into blocks before anything is drawn. Each row is an anchor, so hovering or
/// clicking a row dims the others and clicking pins it, the same way a line chart's series behave.
/// </para>
/// </remarks>
public static class Heatmap {

    private const double LegendHeight = 18 * SvgText.TypeScale;
    private const double SwatchWidth = 14;
    private const double SwatchHeight = 10;
    private const double SwatchGap = 2;

    /// <summary>The most columns one heatmap draws.</summary>
    /// <remarks>
    /// A distribution over time does not need one column per scrape: sixty columns still show the
    /// shape of a day and keep sixteen heatmaps inside the page's markup budget.
    /// </remarks>
    internal const int MaxColumns = 60;

    /// <summary>Converts cumulative series into per-bucket values.</summary>
    /// <param name="cumulative">The <c>le</c> series, in any order.</param>
    /// <returns>
    /// Rows sorted by boundary with <c>+Inf</c> last, each holding the count that fell in that bucket.
    /// A negative difference is clamped to zero, because a counter reset is not a negative count.
    /// </returns>
    public static IReadOnlyList<HeatmapRow> FromCumulative(IReadOnlyList<HeatmapRow> cumulative) {
        ArgumentNullException.ThrowIfNull(cumulative);

        List<HeatmapRow> rows = [];
        Dictionary<DateTimeOffset, double?>? previous = null;

        foreach (HeatmapRow row in cumulative.OrderBy(row => BucketValue(row.Label))) {
            List<ChartPoint> points = [];

            foreach (ChartPoint point in row.Points) {
                double? before = previous is not null && previous.TryGetValue(point.At, out double? cumulativeBefore)
                    ? cumulativeBefore
                    : 0;

                points.Add(new ChartPoint(
                    point.At,
                    point.Value is double current && before is double below
                        ? Math.Max(0, current - below)
                        : point.Value));
            }

            rows.Add(new HeatmapRow(row.Label, points));
            previous = row.Points.ToDictionary(point => point.At, point => point.Value);
        }

        return rows;
    }

    /// <summary>Renders the heatmap as SVG.</summary>
    /// <param name="rows">The per-bucket rows, already differenced.</param>
    /// <param name="x">The time axis; tick positions are seconds after the first sample.</param>
    /// <param name="size">The full drawing area, labels included.</param>
    /// <param name="frame">The drawing's title, bucket unit and words for no data.</param>
    /// <param name="valueUnit">The unit of the cell values, for the legend and the hover text.</param>
    /// <returns>One <c>&lt;svg&gt;</c> element.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size leaves no room to draw in.</exception>
    public static string Render(
        IReadOnlyList<HeatmapRow> rows,
        ChartAxis x,
        ChartSize size,
        ChartFrame frame,
        string valueUnit) {

        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(valueUnit);

        StringBuilder svg = new();
        double band = ChartChrome.Begin(svg, size, "heatmap", frame, $"{frame.Title}: {frame.YTitle} buckets");
        ChartPlot plot = ChartChrome.Plot(size, band + LegendHeight);

        IReadOnlyList<HeatmapRow> folded = Fold(rows, MaxColumns);
        (DateTimeOffset earliest, DateTimeOffset latest) = ChartSamples.Instants(folded.SelectMany(row => row.Points));
        double span = ChartSamples.TimeSpan((latest - earliest).TotalSeconds, x.Ticks);
        int columns = folded.Count == 0 ? 0 : folded.Max(row => row.Points.Count);
        double max = Maximum(folded);

        ChartChrome.WriteAxisTitle(svg, frame.YTitle, plot);

        // The shade legend sits under the title, where the line charts put theirs, so it never lands on
        // the time axis.
        WriteLegend(svg, band, max, valueUnit);
        ChartChrome.WriteTimeLabels(svg, x.Ticks, plot, size, tick => plot.Time(tick, span));
        ChartChrome.Border(svg, plot);

        double cellWidth = columns == 0 ? plot.Width : plot.Width / columns;
        double cellHeight = folded.Count == 0 ? plot.Height : plot.Height / folded.Count;

        // A bucket map with no observations keeps its rows as lines, so the buckets and their order are
        // still readable rather than an empty rectangle.
        WriteRows(svg, folded, frame, valueUnit, plot, cellHeight, cellWidth, max, latest - earliest);

        if (max <= 0) {
            ChartChrome.Empty(svg, plot, frame.EmptyText);
        }

        svg.Append("</svg>\n");

        return svg.ToString();
    }

    /// <summary>Folds an answer into at most <paramref name="cap"/> columns, summing each block.</summary>
    /// <param name="rows">The differenced rows.</param>
    /// <param name="cap">The most columns to draw.</param>
    internal static IReadOnlyList<HeatmapRow> Fold(IReadOnlyList<HeatmapRow> rows, int cap) {
        ArgumentNullException.ThrowIfNull(rows);

        int columns = rows.Count == 0 ? 0 : rows.Max(row => row.Points.Count);
        if (columns <= cap || cap <= 0) {
            return rows;
        }

        int block = (int)Math.Ceiling((double)columns / cap);
        List<HeatmapRow> folded = [];

        foreach (HeatmapRow row in rows) {
            List<ChartPoint> points = [];
            for (int start = 0; start < row.Points.Count; start += block) {
                List<ChartPoint> slice = [.. row.Points.Skip(start).Take(block)];
                double? sum = slice.Any(point => point.Value is not null)
                    ? slice.Sum(point => point.Value ?? 0)
                    : null;
                points.Add(new ChartPoint(slice[^1].At, sum));
            }

            folded.Add(new HeatmapRow(row.Label, points));
        }

        return folded;
    }

    private static void WriteRows(
        StringBuilder svg,
        IReadOnlyList<HeatmapRow> rows,
        ChartFrame frame,
        string valueUnit,
        ChartPlot plot,
        double cellHeight,
        double cellWidth,
        double max,
        TimeSpan span) {

        for (int row = 0; row < rows.Count; row++) {
            HeatmapRow line = rows[row];
            // Rows arrive ordered by boundary; drawing from the bottom up puts the smallest bucket at
            // the bottom and +Inf at the top, which is how a latency heatmap is read.
            double y = plot.Top + ((rows.Count - 1 - row) * cellHeight);
            string id = frame.Id + "-r" + (row + 1).ToString(CultureInfo.InvariantCulture);

            // A row with nothing to shade keeps its edge, so an empty bucket map still shows its scale.
            if (max <= 0) {
                svg.Append("<line class=\"chart-grid\" x1=\"").Append(SvgText.Number(plot.Left))
                    .Append("\" y1=\"").Append(SvgText.Number(y + cellHeight))
                    .Append("\" x2=\"").Append(SvgText.Number(plot.Right))
                    .Append("\" y2=\"").Append(SvgText.Number(y + cellHeight))
                    .Append("\" stroke=\"#e3e3e3\" stroke-width=\"1\"/>\n");
            }

            svg.Append("<a class=\"heat-row\" id=\"").Append(SvgText.Escape(id))
                .Append("\" href=\"#").Append(SvgText.Escape(id)).Append("\" tabindex=\"-1\">\n");
            svg.Append("<title>").Append(SvgText.Escape($"{BucketLabel(line.Label)} {frame.YTitle}")).Append("</title>\n");
            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(plot.Left - 4))
                .Append("\" y=\"").Append(SvgText.Number(y + (cellHeight / 2) + (4 * SvgText.TypeScale)))
                .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
                .Append("\" fill=\"#444444\" text-anchor=\"end\">").Append(SvgText.Escape(BucketLabel(line.Label)))
                .Append("</text>\n");

            WriteRuns(svg, line, frame, valueUnit, span, plot, y, cellHeight, cellWidth, max);
            svg.Append("</a>\n");
        }
    }

    private static void WriteRuns(
        StringBuilder svg,
        HeatmapRow line,
        ChartFrame frame,
        string valueUnit,
        TimeSpan span,
        ChartPlot plot,
        double y,
        double cellHeight,
        double cellWidth,
        double max) {

        int start = -1;
        int level = 0;
        double total = 0;
        DateTimeOffset from = default;
        DateTimeOffset to = default;

        for (int column = 0; column <= line.Points.Count; column++) {
            ChartPoint point = column < line.Points.Count ? line.Points[column] : default;
            int cellLevel = column < line.Points.Count && point.Value is double value && value > 0
                ? Level(value, max)
                : 0;

            if (start >= 0 && cellLevel == level) {
                total += Math.Max(0, point.Value ?? 0);
                to = point.At;
                continue;
            }

            if (start >= 0) {
                WriteRun(svg, line.Label, frame, valueUnit, span, plot, y, cellHeight, cellWidth,
                    start, column - 1, level, total, from, to);
            }

            if (cellLevel > 0) {
                start = column;
                level = cellLevel;
                total = Math.Max(0, point.Value ?? 0);
                from = point.At;
                to = point.At;
            } else {
                start = -1;
            }
        }
    }

    private static void WriteRun(
        StringBuilder svg,
        string label,
        ChartFrame frame,
        string valueUnit,
        TimeSpan span,
        ChartPlot plot,
        double y,
        double cellHeight,
        double cellWidth,
        int start,
        int end,
        int level,
        double total,
        DateTimeOffset from,
        DateTimeOffset to) {

        // No gap: a histogram is contiguous, and its blocks should touch.
        double left = plot.Left + (start * cellWidth);
        double width = (end - start + 1) * cellWidth;
        string when = start == end
            ? ChartChrome.Instant(from, frame.Zone, span)
            : ChartChrome.Instant(from, frame.Zone, span) + "–" + ChartChrome.Instant(to, frame.Zone, span);

        svg.Append("<rect x=\"").Append(SvgText.Number(left))
            .Append("\" y=\"").Append(SvgText.Number(y))
            .Append("\" width=\"").Append(SvgText.Number(width))
            .Append("\" height=\"").Append(SvgText.Number(cellHeight))
            .Append("\" fill=\"").Append(SvgText.HeatFill(level))
            .Append("\"><title>").Append(SvgText.Escape($"{BucketLabel(label)}: {Format(total)} {valueUnit} ({when})"))
            .Append("</title></rect>\n");
    }

    // The shades are a log scale, so the legend gives the two ends of the range and each run names its
    // own value on hover; five printed ranges would be more precise than the colours are.
    private static void WriteLegend(StringBuilder svg, double top, double max, string valueUnit) {
        double y = top + 2;
        double x = ChartChrome.LeftMargin;

        svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(x))
            .Append("\" y=\"").Append(SvgText.Number(y + SwatchHeight))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
            .Append("\" fill=\"#444444\" text-anchor=\"start\">0</text>\n");

        x += 12 * SvgText.TypeScale;
        for (int level = 1; level <= SvgText.HeatLevels; level++) {
            svg.Append("<rect class=\"heat heat-").Append(level).Append(" legend\" x=\"").Append(SvgText.Number(x))
                .Append("\" y=\"").Append(SvgText.Number(y))
                .Append("\" width=\"").Append(SvgText.Number(SwatchWidth))
                .Append("\" height=\"").Append(SvgText.Number(SwatchHeight))
                .Append("\" fill=\"").Append(SvgText.HeatFill(level)).Append("\"/>\n");
            x += SwatchWidth + SwatchGap;
        }

        svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(x + 2))
            .Append("\" y=\"").Append(SvgText.Number(y + SwatchHeight))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
            .Append("\" fill=\"#444444\" text-anchor=\"start\">").Append(Format(max)).Append(' ')
            .Append(SvgText.Escape(valueUnit)).Append("</text>\n");
    }

    private static string Format(double value) => BudgetAmount.Format(value);

    // A bucket boundary arrives as Prometheus wrote it — `0.05`, or `1.048576e+06` for a byte bucket.
    private static string BucketLabel(string label) =>
        label == "+Inf" || !double.TryParse(label, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? label
            : BudgetAmount.Format(value);

    private static double Maximum(IReadOnlyList<HeatmapRow> rows) {
        double max = 0;
        foreach (HeatmapRow row in rows) {
            foreach (ChartPoint point in row.Points) {
                if (point.Value is double value && double.IsFinite(value) && value > max) {
                    max = value;
                }
            }
        }

        return max;
    }

    // A log scale keeps a busy bucket from flattening every quiet one into the palest class.
    private static int Level(double value, double max) {
        if (max <= 0) {
            return SvgText.HeatLevels;
        }

        double normalised = Math.Log(1 + value) / Math.Log(1 + max);

        return Math.Clamp((int)Math.Ceiling(normalised * SvgText.HeatLevels), 1, SvgText.HeatLevels);
    }

    private static double BucketValue(string label) =>
        double.TryParse(label, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : double.PositiveInfinity;
}
