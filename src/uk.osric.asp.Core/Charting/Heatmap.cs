// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Globalization;
using System.Text;

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
/// the cap allows is folded into blocks before anything is drawn.
/// </para>
/// </remarks>
public static class Heatmap {

    private const double LeftMargin = 60;
    private const double RightMargin = 10;
    private const double BottomMargin = 22;
    private const double CellGap = 0.5;
    private const double LegendHeight = 24;
    private const double SwatchWidth = 14;
    private const double SwatchHeight = 10;
    private const double SwatchGap = 2;

    /// <summary>The most columns one heatmap draws.</summary>
    internal const int MaxColumns = 120;

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

        double plotWidth = size.Width - LeftMargin - RightMargin;
        if (plotWidth <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The heatmap is too small for its labels.");
        }

        StringBuilder svg = new();
        double top = ChartChrome.Begin(svg, size, "heatmap", frame, $"{frame.Title}: {frame.YTitle} buckets");
        double plotHeight = size.Height - top - BottomMargin - LegendHeight;
        if (plotHeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The heatmap is too small for its labels.");
        }

        IReadOnlyList<HeatmapRow> folded = Fold(rows, MaxColumns);
        (DateTimeOffset earliest, DateTimeOffset latest) = ChartSamples.Instants(folded.SelectMany(row => row.Points));
        double span = Math.Max((latest - earliest).TotalSeconds, 1);
        int columns = folded.Count == 0 ? 0 : folded.Max(row => row.Points.Count);
        double max = Maximum(folded);

        WriteAxisTitle(svg, frame.YTitle, top, plotHeight);

        if (columns == 0 || max <= 0) {
            ChartChrome.Empty(svg, size, top, frame.EmptyText);
            svg.Append("</svg>\n");

            return svg.ToString();
        }

        double cellWidth = plotWidth / columns;
        double cellHeight = plotHeight / folded.Count;

        WriteRows(svg, folded, frame, valueUnit, top, cellHeight, cellWidth, max, earliest, latest - earliest);

        foreach (ChartTick tick in x.Ticks.OrderBy(tick => tick.Position)) {
            double at = LeftMargin + ((tick.Position / span) * plotWidth);
            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(at))
                .Append("\" y=\"").Append(SvgText.Number(top + plotHeight + 14))
                .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
                .Append("\" fill=\"#444444\" text-anchor=\"middle\">").Append(SvgText.Escape(tick.Label))
                .Append("</text>\n");
        }

        WriteLegend(svg, top + plotHeight, max, valueUnit);
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
        double top,
        double cellHeight,
        double cellWidth,
        double max,
        DateTimeOffset earliest,
        TimeSpan span) {

        for (int row = 0; row < rows.Count; row++) {
            HeatmapRow line = rows[row];
            // Rows arrive ordered by boundary; drawing from the bottom up puts the smallest bucket at
            // the bottom and +Inf at the top, which is how a latency heatmap is read.
            double y = top + ((rows.Count - 1 - row) * cellHeight);

            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(LeftMargin - 4))
                .Append("\" y=\"").Append(SvgText.Number(y + (cellHeight / 2) + 4))
                .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
                .Append("\" fill=\"#444444\" text-anchor=\"end\">").Append(SvgText.Escape(line.Label))
                .Append("</text>\n");

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
                    WriteRun(svg, line.Label, frame, valueUnit, earliest, span, top, y, cellHeight, cellWidth,
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
    }

    private static void WriteRun(
        StringBuilder svg,
        string label,
        ChartFrame frame,
        string valueUnit,
        DateTimeOffset earliest,
        TimeSpan span,
        double top,
        double y,
        double cellHeight,
        double cellWidth,
        int start,
        int end,
        int level,
        double total,
        DateTimeOffset from,
        DateTimeOffset to) {

        double left = LeftMargin + (start * cellWidth);
        double width = ((end - start + 1) * cellWidth) - (CellGap * 2);
        string when = start == end
            ? ChartChrome.Instant(from, frame.Zone, span)
            : ChartChrome.Instant(from, frame.Zone, span) + "–" + ChartChrome.Instant(to, frame.Zone, span);

        svg.Append("<rect class=\"heat heat-").Append(level)
            .Append("\" x=\"").Append(SvgText.Number(left + CellGap))
            .Append("\" y=\"").Append(SvgText.Number(y + CellGap))
            .Append("\" width=\"").Append(SvgText.Number(width))
            .Append("\" height=\"").Append(SvgText.Number(cellHeight - (CellGap * 2)))
            .Append("\" fill=\"").Append(SvgText.HeatFill(level))
            .Append("\"><title>").Append(SvgText.Escape($"{label} {frame.YTitle}: {Format(total)} {valueUnit} ({when})"))
            .Append("</title></rect>\n");
    }

    private static void WriteAxisTitle(StringBuilder svg, string yTitle, double top, double plotHeight) {
        double middle = top + (plotHeight / 2);

        svg.Append("<text class=\"chart-axis-title\" transform=\"rotate(-90 12 ").Append(SvgText.Number(middle))
            .Append(")\" x=\"12\" y=\"").Append(SvgText.Number(middle))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
            .Append("\" fill=\"#666666\" text-anchor=\"middle\">").Append(SvgText.Escape(yTitle))
            .Append("</text>\n");
    }

    // The shades are a log scale, so the legend gives the two ends of the range and each run names its
    // own value on hover; five printed ranges would be more precise than the colours are.
    private static void WriteLegend(StringBuilder svg, double top, double max, string valueUnit) {
        double y = top + 4;
        double x = LeftMargin;

        svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(x))
            .Append("\" y=\"").Append(SvgText.Number(y + SwatchHeight))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
            .Append("\" fill=\"#444444\" text-anchor=\"start\">0</text>\n");

        x += 12;
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

    private static string Format(double value) =>
        value < 1
            ? value.ToString("0.####", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

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
