// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using System.Globalization;
using System.Text;

/// <summary>Turns cumulative histogram counters into a heatmap.</summary>
/// <remarks>
/// Prometheus exposes an explicit-bucket histogram as cumulative counters, one series per <c>le</c>
/// boundary. The count in a bucket is the difference between its boundary and the one below it, and
/// drawing the cumulative values would show every row as a superset of the row above.
/// </remarks>
public static class Heatmap {

    private const double LeftMargin = 60;
    private const double RightMargin = 10;
    private const double TopMargin = 10;
    private const double BottomMargin = 20;
    private const int Levels = 5;
    private const double CellGap = 0.5;
    private const double LegendHeight = 24;
    private const double SwatchWidth = 14;
    private const double SwatchHeight = 10;
    private const double SwatchGap = 2;

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
    /// <param name="title">The chart title, escaped where it is drawn.</param>
    /// <param name="yTitle">The unit of the bucket boundaries, drawn up the left edge.</param>
    /// <returns>One <c>&lt;svg&gt;</c> element.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size leaves no room to draw in.</exception>
    public static string Render(IReadOnlyList<HeatmapRow> rows, ChartAxis x, ChartSize size, string title, string yTitle) {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(yTitle);

        double plotWidth = size.Width - LeftMargin - RightMargin;
        double plotHeight = size.Height - TopMargin - BottomMargin - LegendHeight;
        if (plotWidth <= 0 || plotHeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The heatmap is too small for its labels.");
        }

        DateTimeOffset earliest = Earliest(rows);
        double span = Math.Max((Latest(rows) - earliest).TotalSeconds, 1);
        int columns = rows.Count == 0 ? 0 : rows.Max(row => row.Points.Count);
        double max = Maximum(rows);

        double cellWidth = columns == 0 ? 0 : plotWidth / columns;
        double cellHeight = rows.Count == 0 ? 0 : plotHeight / rows.Count;

        StringBuilder svg = new();
        svg.Append("<svg class=\"heatmap\" viewBox=\"0 0 ").Append(Number(size.Width)).Append(' ')
            .Append(Number(size.Height)).Append("\" role=\"img\">\n");
        svg.Append("<title>").Append(SvgText.Escape(title)).Append("</title>\n");
        svg.Append("<text class=\"chart-axis-title\" transform=\"rotate(-90 12 ")
            .Append(Number(TopMargin + (plotHeight / 2)))
            .Append(")\" x=\"12\" y=\"").Append(Number(TopMargin + (plotHeight / 2)))
            .Append("\" text-anchor=\"middle\">").Append(SvgText.Escape(yTitle)).Append("</text>\n");

        for (int row = 0; row < rows.Count; row++) {
            HeatmapRow line = rows[row];
            // Rows arrive ordered by boundary; drawing from the bottom up puts the smallest bucket at
            // the bottom and +Inf at the top, which is how a latency heatmap is read.
            double top = TopMargin + ((rows.Count - 1 - row) * cellHeight);

            svg.Append("<text class=\"chart-label\" x=\"").Append(Number(LeftMargin - 4))
                .Append("\" y=\"").Append(Number(top + (cellHeight / 2) + 4))
                .Append("\" text-anchor=\"end\">").Append(SvgText.Escape(line.Label)).Append("</text>\n");

            for (int column = 0; column < line.Points.Count; column++) {
                if (line.Points[column].Value is not double value || value <= 0) {
                    continue;
                }

                double left = LeftMargin + (column * cellWidth);
                svg.Append("<rect class=\"heat heat-").Append(Level(value, max))
                    .Append("\" x=\"").Append(Number(left + CellGap))
                    .Append("\" y=\"").Append(Number(top + CellGap))
                    .Append("\" width=\"").Append(Number(cellWidth - (CellGap * 2)))
                    .Append("\" height=\"").Append(Number(cellHeight - (CellGap * 2)))
                    .Append("\"><title>").Append(SvgText.Escape($"{line.Label} {yTitle}: {Format(value)}"))
                    .Append("</title></rect>\n");
            }
        }

        foreach (ChartTick tick in x.Ticks.OrderBy(tick => tick.Position)) {
            double at = LeftMargin + ((tick.Position / span) * plotWidth);
            svg.Append("<text class=\"chart-label\" x=\"").Append(Number(at))
                .Append("\" y=\"").Append(Number(TopMargin + plotHeight + 14))
                .Append("\" text-anchor=\"middle\">").Append(SvgText.Escape(tick.Label)).Append("</text>\n");
        }

        WriteLegend(svg, size, max);
        svg.Append("</svg>\n");
        return svg.ToString();
    }

    // The shades are a log scale, so the legend gives the two ends of the range and each cell names
    // its own value on hover; five printed ranges would be more precise than the colours are.
    private static void WriteLegend(StringBuilder svg, ChartSize size, double max) {
        double top = size.Height - LegendHeight + 4;
        double left = LeftMargin;

        svg.Append("<text class=\"chart-label\" x=\"").Append(Number(left))
            .Append("\" y=\"").Append(Number(top + SwatchHeight))
            .Append("\" text-anchor=\"start\">0</text>\n");

        left += 12;
        for (int level = 1; level <= Levels; level++) {
            svg.Append("<rect class=\"heat heat-").Append(level).Append(" legend\" x=\"").Append(Number(left))
                .Append("\" y=\"").Append(Number(top))
                .Append("\" width=\"").Append(Number(SwatchWidth))
                .Append("\" height=\"").Append(Number(SwatchHeight)).Append("\"/>\n");
            left += SwatchWidth + SwatchGap;
        }

        svg.Append("<text class=\"chart-label\" x=\"").Append(Number(left + 2))
            .Append("\" y=\"").Append(Number(top + SwatchHeight))
            .Append("\" text-anchor=\"start\">").Append(Format(max)).Append("</text>\n");
    }

    private static string Format(double value) =>
        value < 1
            ? value.ToString("0.####", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static DateTimeOffset Earliest(IReadOnlyList<HeatmapRow> rows) {
        DateTimeOffset earliest = default;
        bool any = false;

        foreach (HeatmapRow row in rows) {
            foreach (ChartPoint point in row.Points) {
                if (!any || point.At < earliest) {
                    earliest = point.At;
                    any = true;
                }
            }
        }

        return earliest;
    }

    private static DateTimeOffset Latest(IReadOnlyList<HeatmapRow> rows) {
        DateTimeOffset latest = default;
        bool any = false;

        foreach (HeatmapRow row in rows) {
            foreach (ChartPoint point in row.Points) {
                if (!any || point.At > latest) {
                    latest = point.At;
                    any = true;
                }
            }
        }

        return latest;
    }

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
            return Levels;
        }

        double normalised = Math.Log(1 + value) / Math.Log(1 + max);
        return Math.Clamp((int)Math.Ceiling(normalised * Levels), 1, Levels);
    }

    private static double BucketValue(string label) =>
        double.TryParse(label, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : double.PositiveInfinity;

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
