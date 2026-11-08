// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

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
        svg.Append("<svg class=\"chart\" viewBox=\"0 0 ").Append(SvgText.Number(size.Width)).Append(' ')
            .Append(SvgText.Number(size.Height)).Append("\" role=\"img\">\n");
        svg.Append("<title>").Append(SvgText.Escape(Title(series))).Append("</title>\n");
        svg.Append("<desc>").Append(SvgText.Escape(y.Title)).Append(" over ")
            .Append(SvgText.Escape(x.Title)).Append("</desc>\n");
        // The unit belongs on the axis, not only in the description: a chart of unknown units is a
        // chart nobody can act on.
        svg.Append("<text class=\"chart-axis-title\" transform=\"rotate(-90 10 ")
            .Append(SvgText.Number(TopMargin + (plotHeight / 2)))
            .Append(")\" x=\"10\" y=\"").Append(SvgText.Number(TopMargin + (plotHeight / 2)))
            .Append("\" text-anchor=\"middle\">").Append(SvgText.Escape(y.Title)).Append("</text>\n");

        foreach (ChartTick tick in Order(y.Ticks)) {
            double at = ValuePosition(tick.Position, min, max, plotHeight);
            svg.Append("<line class=\"chart-grid\" x1=\"").Append(SvgText.Number(LeftMargin))
                .Append("\" y1=\"").Append(SvgText.Number(at))
                .Append("\" x2=\"").Append(SvgText.Number(LeftMargin + plotWidth))
                .Append("\" y2=\"").Append(SvgText.Number(at)).Append("\"/>\n");
            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(LeftMargin - 4))
                .Append("\" y=\"").Append(SvgText.Number(at + 4))
                .Append("\" text-anchor=\"end\">").Append(SvgText.Escape(tick.Label)).Append("</text>\n");
        }

        foreach (ChartTick tick in Order(x.Ticks)) {
            double at = LeftMargin + ((tick.Position / span) * plotWidth);
            svg.Append("<line class=\"chart-grid\" x1=\"").Append(SvgText.Number(at))
                .Append("\" y1=\"").Append(SvgText.Number(TopMargin))
                .Append("\" x2=\"").Append(SvgText.Number(at))
                .Append("\" y2=\"").Append(SvgText.Number(TopMargin + plotHeight)).Append("\"/>\n");
            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(at))
                .Append("\" y=\"").Append(SvgText.Number(size.Height - 6))
                .Append("\" text-anchor=\"middle\">").Append(SvgText.Escape(tick.Label)).Append("</text>\n");
        }

        for (int index = 0; index < series.Count; index++) {
            ChartSeries line = series[index];
            string colour = SvgText.SeriesColour(index);
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
                .Append("\" cx=\"").Append(SvgText.Number(x))
                .Append("\" cy=\"").Append(SvgText.Number(y))
                .Append("\" r=\"").Append(SvgText.Number(PointRadius)).Append("\"/>\n");
            return;
        }

        if (run.Count > 1) {
            svg.Append("<polyline class=\"series ").Append(colour).Append("\" points=\"")
                .Append(string.Join(' ', run.Select(point => SvgText.Number(point.X) + "," + SvgText.Number(point.Y))))
                .Append("\"/>\n");
        }
    }

    private static (DateTimeOffset Start, double Span) TimeRange(IReadOnlyList<ChartSeries> series) {
        (DateTimeOffset earliest, DateTimeOffset latest) =
            ChartSamples.Instants(series.SelectMany(line => line.Points));

        return (earliest, Math.Max((latest - earliest).TotalSeconds, 1));
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
            return ChartSamples.Widen(min, max);
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
}
