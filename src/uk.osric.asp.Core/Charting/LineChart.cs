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
/// label at the end of its line, and a legend inside the drawing. Each series' legend entry sits in the
/// same anchor as the line itself, so hovering or clicking either one highlights the same series.
/// </para>
/// </remarks>
public static class LineChart {

    private const double PointRadius = 2;
    private const double LabelHeight = 10 * SvgText.TypeScale;

    /// <summary>Renders one chart.</summary>
    /// <param name="series">The lines to draw; a null value is a gap rather than a zero.</param>
    /// <param name="x">The time axis; tick positions are seconds after the first sample.</param>
    /// <param name="y">
    /// The value axis; tick positions are the values themselves, and its divisor rescales the samples
    /// into the unit the ticks are labelled in.
    /// </param>
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

        StringBuilder svg = new();
        ChartPlot plot = ChartChrome.BeginPlot(svg, size, "chart", frame, ChartChrome.Describe(x, y),
            [.. series.Select(line => line.Label)], out IReadOnlyList<LegendEntry> legend);

        (DateTimeOffset start, double span) = TimeRange(series, x);
        (double min, double max) = ValueRange(series, y);

        ChartChrome.WriteAxisTitle(svg, y.Title, plot);
        ChartChrome.WriteValueGrid(svg, y.Ticks, plot, tick => plot.Value(tick, min, max));
        ChartChrome.WriteTimeGrid(svg, x.Ticks, plot, size, tick => plot.Time(tick, span));
        ChartChrome.Border(svg, plot);

        if (!HasSamples(series, y)) {
            ChartChrome.Empty(svg, plot, frame.EmptyText);
            svg.Append("</svg>\n");

            return svg.ToString();
        }

        List<(double X, double Y)> labels = [];

        for (int index = 0; index < series.Count; index++) {
            ChartSeries line = series[index];
            string colour = SvgText.SeriesStroke(index);
            string dash = SvgText.SeriesDash(index) is { } pattern
                ? " stroke-dasharray=\"" + pattern + "\""
                : string.Empty;

            ChartChrome.BeginSeries(svg, frame, index, line.Label, legend[index]);
            WriteLine(svg, line, index, y, colour, dash, series.Count > 1, start, span, min, max, plot);
            ChartChrome.EndSeries(svg);

            WriteEndLabel(svg, line, y, colour, start, span, min, max, plot, labels);
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static void WriteLine(
        StringBuilder svg,
        ChartSeries line,
        int index,
        ChartAxis y,
        string colour,
        string dash,
        bool hitStroke,
        DateTimeOffset start,
        double span,
        double min,
        double max,
        ChartPlot plot) {

        List<(double X, double Y)> run = [];

        foreach (ChartPoint point in line.Points) {
            if (y.Scale(point.Value) is not double value || !double.IsFinite(value)) {
                WriteRun(svg, run, index, colour, dash, hitStroke);
                run = [];
                continue;
            }

            run.Add((plot.Time((point.At - start).TotalSeconds, span), plot.Value(value, min, max)));
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
        ChartAxis axis,
        string colour,
        DateTimeOffset start,
        double span,
        double min,
        double max,
        ChartPlot plot,
        List<(double X, double Y)> placed) {

        if (string.IsNullOrWhiteSpace(line.Label)) {
            return;
        }

        ChartPoint? last = null;
        foreach (ChartPoint point in line.Points) {
            if (axis.Scale(point.Value) is double value && double.IsFinite(value)) {
                last = point;
            }
        }

        if (last is not ChartPoint sample || axis.Scale(sample.Value) is not double lastValue) {
            return;
        }

        double x = plot.Time((sample.At - start).TotalSeconds, span);
        double y = plot.Value(lastValue, min, max);
        if (placed.Any(other => Math.Abs(other.Y - y) < LabelHeight)) {
            return;
        }

        double width = SvgText.EstimateWidth(line.Label);
        double left = Math.Min(x + (4 * SvgText.TypeScale), plot.Right - width);

        placed.Add((left, y));

        svg.Append("<text class=\"series-label\" x=\"").Append(SvgText.Number(left))
            .Append("\" y=\"").Append(SvgText.Number(y + (3 * SvgText.TypeScale)))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.SeriesFontSize))
            .Append("\" fill=\"").Append(colour).Append("\" text-anchor=\"start\">")
            .Append(SvgText.Escape(line.Label)).Append("</text>\n");
    }

    private static (DateTimeOffset Start, double Span) TimeRange(IReadOnlyList<ChartSeries> series, ChartAxis x) {
        (DateTimeOffset earliest, DateTimeOffset latest) =
            ChartSamples.Instants(series.SelectMany(line => line.Points));

        return (earliest, ChartSamples.TimeSpan((latest - earliest).TotalSeconds, x.Ticks));
    }

    private static (double Min, double Max) ValueRange(IReadOnlyList<ChartSeries> series, ChartAxis y) {
        // The ticks are already in the axis' unit, so they win; the samples are rescaled to match.
        (double min, double max) = Extremes(y.Ticks.Select(tick => (double?)tick.Position));

        if (min > max) {
            (min, max) = Extremes(series.SelectMany(line => line.Points).Select(point => y.Scale(point.Value)));
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

    private static bool HasSamples(IReadOnlyList<ChartSeries> series, ChartAxis y) =>
        series.Any(line => line.Points.Any(point => y.Scale(point.Value) is double value && double.IsFinite(value)));
}
