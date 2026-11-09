// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Text;

/// <summary>Draws line charts as SVG.</summary>
/// <remarks>
/// <para>
/// Pure and deterministic: the same input produces the same string, so the tests assert on markup.
/// Every visual value is a presentation attribute, so a drawing served on its own is legible without a
/// stylesheet; the classes are hooks for the page's hover and pinned states.
/// </para>
/// <para>
/// A series is identified four ways, in the order a reader meets them: its colour, its dash pattern, a
/// label at the end of its line, and a legend inside the drawing. Colour is the last cue, not the
/// first.
/// </para>
/// </remarks>
public static class LineChart {

    private const double LeftMargin = 40;
    private const double RightMargin = 12;
    private const double BottomMargin = 22;
    private const double PointRadius = 2;
    private const double LabelHeight = 10;

    /// <summary>Renders one chart.</summary>
    /// <param name="series">The lines to draw; a null value is a gap rather than a zero.</param>
    /// <param name="x">The time axis; tick positions are seconds after the first sample.</param>
    /// <param name="y">The value axis; tick positions are the values themselves.</param>
    /// <param name="size">The full drawing area, labels included.</param>
    /// <param name="frame">The drawing's title, axis title and words for no data.</param>
    /// <returns>One <c>&lt;svg&gt;</c> element, with every label escaped.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size leaves no room to plot in.</exception>
    public static string Render(
        IReadOnlyList<ChartSeries> series,
        ChartAxis x,
        ChartAxis y,
        ChartSize size,
        ChartFrame frame) {

        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(frame);

        double plotWidth = size.Width - LeftMargin - RightMargin;
        if (plotWidth <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The chart is too small for its labels.");
        }

        StringBuilder svg = new();
        double top = ChartChrome.Begin(svg, size, "chart", frame, ChartChrome.Describe(x, y));
        top = ChartChrome.Legend(svg, size, top, [.. series.Select(line => line.Label)]);

        double plotHeight = size.Height - top - BottomMargin;
        if (plotHeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The chart is too small for its labels.");
        }

        (DateTimeOffset start, double span) = TimeRange(series);
        (double min, double max) = ValueRange(series, y);

        WriteAxisTitles(svg, y, top, plotHeight);

        foreach (ChartTick tick in Order(y.Ticks)) {
            double at = ValuePosition(tick.Position, min, max, top, plotHeight);
            svg.Append("<line class=\"chart-grid\" x1=\"").Append(SvgText.Number(LeftMargin))
                .Append("\" y1=\"").Append(SvgText.Number(at))
                .Append("\" x2=\"").Append(SvgText.Number(LeftMargin + plotWidth))
                .Append("\" y2=\"").Append(SvgText.Number(at))
                .Append("\" stroke=\"#e3e3e3\" stroke-width=\"1\"/>\n");
            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(LeftMargin - 4))
                .Append("\" y=\"").Append(SvgText.Number(at + 4))
                .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
                .Append("\" fill=\"#444444\" text-anchor=\"end\">").Append(SvgText.Escape(tick.Label))
                .Append("</text>\n");
        }

        foreach (ChartTick tick in Order(x.Ticks)) {
            double at = LeftMargin + ((tick.Position / span) * plotWidth);
            svg.Append("<line class=\"chart-grid\" x1=\"").Append(SvgText.Number(at))
                .Append("\" y1=\"").Append(SvgText.Number(top))
                .Append("\" x2=\"").Append(SvgText.Number(at))
                .Append("\" y2=\"").Append(SvgText.Number(top + plotHeight))
                .Append("\" stroke=\"#e3e3e3\" stroke-width=\"1\"/>\n");
            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(at))
                .Append("\" y=\"").Append(SvgText.Number(size.Height - 6))
                .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
                .Append("\" fill=\"#444444\" text-anchor=\"middle\">").Append(SvgText.Escape(tick.Label))
                .Append("</text>\n");
        }

        if (!HasSamples(series)) {
            ChartChrome.Empty(svg, size, top, frame.EmptyText);
            svg.Append("</svg>\n");

            return svg.ToString();
        }

        double right = LeftMargin + plotWidth;
        List<(double X, double Y)> labels = [];

        for (int index = 0; index < series.Count; index++) {
            ChartSeries line = series[index];
            string colour = SvgText.SeriesStroke(index);
            string dash = SvgText.SeriesDash(index) is { } pattern
                ? " stroke-dasharray=\"" + pattern + "\""
                : string.Empty;
            string id = frame.Id + "-s" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

            svg.Append("<a class=\"series-link\" id=\"").Append(SvgText.Escape(id))
                .Append("\" href=\"#").Append(SvgText.Escape(id)).Append("\" tabindex=\"-1\">\n");
            svg.Append("<title>").Append(SvgText.Escape(line.Label)).Append("</title>\n");

            WriteLine(svg, line, index, colour, dash, series.Count > 1, start, span, min, max, top, plotHeight, plotWidth, right);
            svg.Append("</a>\n");

            WriteEndLabel(svg, line, colour, start, span, min, max, top, plotHeight, plotWidth, right, labels);
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static void WriteAxisTitles(StringBuilder svg, ChartAxis y, double top, double plotHeight) {
        double middle = top + (plotHeight / 2);

        // The unit belongs on the axis, not only in the description: a chart of unknown units is a
        // chart nobody can act on.
        svg.Append("<text class=\"chart-axis-title\" transform=\"rotate(-90 10 ").Append(SvgText.Number(middle))
            .Append(")\" x=\"10\" y=\"").Append(SvgText.Number(middle))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
            .Append("\" fill=\"#666666\" text-anchor=\"middle\">").Append(SvgText.Escape(y.Title))
            .Append("</text>\n");
    }

    private static void WriteLine(
        StringBuilder svg,
        ChartSeries line,
        int index,
        string colour,
        string dash,
        bool hitStroke,
        DateTimeOffset start,
        double span,
        double min,
        double max,
        double top,
        double plotHeight,
        double plotWidth,
        double right) {

        List<(double X, double Y)> run = [];

        foreach (ChartPoint point in line.Points) {
            if (point.Value is not double value || !double.IsFinite(value)) {
                WriteRun(svg, run, index, colour, dash, hitStroke);
                run = [];
                continue;
            }

            double px = Math.Min(right, LeftMargin + ((point.At - start).TotalSeconds / span * plotWidth));
            run.Add((px, ValuePosition(value, min, max, top, plotHeight)));
        }

        WriteRun(svg, run, index, colour, dash, hitStroke);
    }

    // The hit stroke is what makes a 2px line clickable: a second, transparent copy of the same path,
    // drawn only where a click has to choose between lines.
    private static void WriteRun(
        StringBuilder svg,
        List<(double X, double Y)> run,
        int index,
        string colour,
        string dash,
        bool hitStroke) {

        if (run.Count == 0) {
            return;
        }

        string points = string.Join(' ', run.Select(point => SvgText.Number(point.X) + "," + SvgText.Number(point.Y)));
        string shape = "class=\"series " + SvgText.SeriesColour(index) + "\"";

        if (run.Count == 1) {
            (double x, double y) = run[0];
            svg.Append("<circle ").Append(shape)
                .Append(" cx=\"").Append(SvgText.Number(x))
                .Append("\" cy=\"").Append(SvgText.Number(y))
                .Append("\" r=\"").Append(SvgText.Number(PointRadius))
                .Append("\" fill=\"").Append(colour).Append("\"/>\n");

            return;
        }

        svg.Append("<polyline ").Append(shape).Append(" points=\"").Append(points)
            .Append("\" fill=\"none\" stroke=\"").Append(colour)
            .Append("\" stroke-width=\"2\"").Append(dash).Append("/>\n");

        if (hitStroke) {
            svg.Append("<polyline class=\"series-hit\" points=\"").Append(points)
                .Append("\" fill=\"none\" stroke=\"transparent\" stroke-width=\"12\"/>\n");
        }
    }
    private static void WriteEndLabel(
        StringBuilder svg,
        ChartSeries line,
        string colour,
        DateTimeOffset start,
        double span,
        double min,
        double max,
        double top,
        double plotHeight,
        double plotWidth,
        double right,
        List<(double X, double Y)> placed) {

        if (string.IsNullOrWhiteSpace(line.Label)) {
            return;
        }

        ChartPoint? last = null;
        foreach (ChartPoint point in line.Points) {
            if (point.Value is double value && double.IsFinite(value)) {
                last = point;
            }
        }

        if (last is not ChartPoint sample || sample.Value is not double lastValue) {
            return;
        }

        double x = Math.Min(right, 40 + ((sample.At - start).TotalSeconds / span * plotWidth));
        double y = ValuePosition(lastValue, min, max, top, plotHeight);
        if (placed.Any(other => Math.Abs(other.Y - y) < LabelHeight)) {
            return;
        }

        double width = SvgText.EstimateWidth(line.Label);
        double left = Math.Min(x + 4, right - width);

        placed.Add((left, y));

        svg.Append("<text class=\"series-label\" x=\"").Append(SvgText.Number(left))
            .Append("\" y=\"").Append(SvgText.Number(y + 3))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize - 1))
            .Append("\" fill=\"").Append(colour).Append("\" text-anchor=\"start\">")
            .Append(SvgText.Escape(line.Label)).Append("</text>\n");
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

    private static bool HasSamples(IReadOnlyList<ChartSeries> series) =>
        series.Any(line => line.Points.Any(point => point.Value is double value && double.IsFinite(value)));

    private static IEnumerable<ChartTick> Order(IReadOnlyList<ChartTick> ticks) =>
        ticks.OrderBy(tick => tick.Position);

    private static double ValuePosition(double value, double min, double max, double top, double plotHeight) =>
        top + ((1 - ((value - min) / (max - min))) * plotHeight);
}
