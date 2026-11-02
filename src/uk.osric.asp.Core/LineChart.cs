// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using System.Globalization;
using System.Text;

/// <summary>Draws line charts as SVG.</summary>
/// <remarks>
/// Pure and deterministic: the same input produces the same string, so the tests assert on markup.
/// Colour comes from the <c>series-1</c>..<c>series-5</c> classes rather than from here, so the chart
/// inherits the site's palette and a re-theme touches CSS only.
/// </remarks>
public static class LineChart {

    private const double LeftMargin = 40;
    private const double RightMargin = 10;
    private const double TopMargin = 10;
    private const double BottomMargin = 20;
    private const double PointRadius = 2;
    private const int SeriesColours = 5;

    /// <summary>Renders one chart.</summary>
    /// <param name="series">The lines to draw; a null value is a gap rather than a zero.</param>
    /// <param name="x">The time axis; tick positions are seconds after the first sample.</param>
    /// <param name="y">The value axis; tick positions are the values themselves.</param>
    /// <param name="size">The full drawing area, labels included.</param>
    /// <returns>One <c>&lt;svg&gt;</c> element, with every label escaped.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size leaves no room to plot in.</exception>
    public static string Render(IReadOnlyList<ChartSeries> series, ChartAxis x, ChartAxis y, ChartSize size) {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        double plotWidth = size.Width - LeftMargin - RightMargin;
        double plotHeight = size.Height - TopMargin - BottomMargin;
        if (plotWidth <= 0 || plotHeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The chart is too small for its labels.");
        }

        (DateTimeOffset start, double span) = TimeRange(series);
        (double min, double max) = ValueRange(series, y);

        StringBuilder svg = new();
        svg.Append("<svg class=\"chart\" viewBox=\"0 0 ").Append(Number(size.Width)).Append(' ')
            .Append(Number(size.Height)).Append("\" role=\"img\">\n");
        svg.Append("<title>").Append(SvgText.Escape(Title(series))).Append("</title>\n");
        svg.Append("<desc>").Append(SvgText.Escape(y.Title)).Append(" over ")
            .Append(SvgText.Escape(x.Title)).Append("</desc>\n");

        foreach (ChartTick tick in Order(y.Ticks)) {
            double at = ValuePosition(tick.Position, min, max, plotHeight);
            svg.Append("<line class=\"chart-grid\" x1=\"").Append(Number(LeftMargin))
                .Append("\" y1=\"").Append(Number(at))
                .Append("\" x2=\"").Append(Number(LeftMargin + plotWidth))
                .Append("\" y2=\"").Append(Number(at)).Append("\"/>\n");
            svg.Append("<text class=\"chart-label\" x=\"").Append(Number(LeftMargin - 4))
                .Append("\" y=\"").Append(Number(at + 4))
                .Append("\" text-anchor=\"end\">").Append(SvgText.Escape(tick.Label)).Append("</text>\n");
        }

        foreach (ChartTick tick in Order(x.Ticks)) {
            double at = LeftMargin + ((tick.Position / span) * plotWidth);
            svg.Append("<line class=\"chart-grid\" x1=\"").Append(Number(at))
                .Append("\" y1=\"").Append(Number(TopMargin))
                .Append("\" x2=\"").Append(Number(at))
                .Append("\" y2=\"").Append(Number(TopMargin + plotHeight)).Append("\"/>\n");
            svg.Append("<text class=\"chart-label\" x=\"").Append(Number(at))
                .Append("\" y=\"").Append(Number(size.Height - 6))
                .Append("\" text-anchor=\"middle\">").Append(SvgText.Escape(tick.Label)).Append("</text>\n");
        }

        for (int index = 0; index < series.Count; index++) {
            ChartSeries line = series[index];
            string colour = "series-" + ((index % SeriesColours) + 1);
            List<(double X, double Y)> run = [];

            foreach (ChartPoint point in line.Points) {
                if (point.Value is not double value || !double.IsFinite(value)) {
                    WriteRun(svg, run, colour);
                    run = [];
                    continue;
                }

                double px = LeftMargin + ((point.At - start).TotalSeconds / span * plotWidth);
                run.Add((px, ValuePosition(value, min, max, plotHeight)));
            }

            WriteRun(svg, run, colour);
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static void WriteRun(StringBuilder svg, List<(double X, double Y)> run, string colour) {
        if (run.Count == 1) {
            (double x, double y) = run[0];
            svg.Append("<circle class=\"series ").Append(colour)
                .Append("\" cx=\"").Append(Number(x))
                .Append("\" cy=\"").Append(Number(y))
                .Append("\" r=\"").Append(Number(PointRadius)).Append("\"/>\n");
            return;
        }

        if (run.Count > 1) {
            svg.Append("<polyline class=\"series ").Append(colour).Append("\" points=\"")
                .Append(string.Join(' ', run.Select(point => Number(point.X) + "," + Number(point.Y))))
                .Append("\"/>\n");
        }
    }

    private static (DateTimeOffset Start, double Span) TimeRange(IReadOnlyList<ChartSeries> series) {
        bool any = false;
        DateTimeOffset earliest = default;
        DateTimeOffset latest = default;

        foreach (ChartSeries line in series) {
            foreach (ChartPoint point in line.Points) {
                if (!any) {
                    earliest = latest = point.At;
                    any = true;
                    continue;
                }

                earliest = point.At < earliest ? point.At : earliest;
                latest = point.At > latest ? point.At : latest;
            }
        }

        return any ? (earliest, Math.Max((latest - earliest).TotalSeconds, 1)) : (DateTimeOffset.UnixEpoch, 1);
    }

    private static (double Min, double Max) ValueRange(IReadOnlyList<ChartSeries> series, ChartAxis y) {
        (double min, double max) = Extremes(y.Ticks.Select(tick => (double?)tick.Position));

        if (min > max) {
            (min, max) = Extremes(series.SelectMany(line => line.Points).Select(point => point.Value));
        }

        if (min > max) {
            return (0, 1);
        }

        if (min == max) {
            double pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
            return (min - pad, max + pad);
        }

        return (min, max);
    }

    // min > max means "nothing to measure", which keeps the caller's fallbacks in one place.
    private static (double Min, double Max) Extremes(IEnumerable<double?> values) {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        foreach (double? candidate in values) {
            if (candidate is not double value || !double.IsFinite(value)) {
                continue;
            }

            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        return (min, max);
    }

    private static IEnumerable<ChartTick> Order(IReadOnlyList<ChartTick> ticks) =>
        ticks.OrderBy(tick => tick.Position);

    private static double ValuePosition(double value, double min, double max, double plotHeight) =>
        TopMargin + ((1 - ((value - min) / (max - min))) * plotHeight);

    private static string Title(IReadOnlyList<ChartSeries> series) {
        string[] labels = [.. series
            .Select(line => line.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))];

        return labels.Length == 0 ? "Chart" : string.Join(", ", labels);
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
