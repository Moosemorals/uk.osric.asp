// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Text;

/// <summary>Draws a stacked area chart as SVG.</summary>
/// <remarks>
/// <para>
/// A stacked area is for the panels where the labels are parts of one total: the sum is the top edge,
/// and a reader can see both the whole and one part without switching charts. It is not a way to draw
/// any line chart — a line whose labels do not partition the total belongs in <see cref="LineChart"/>.
/// </para>
/// <para>
/// A missing sample is zero, not a gap: for the counters this chart is for, a step with no series
/// means no events in that step, and a hole in the middle of a stack would break every band above it.
/// A negative value is refused rather than drawn as a shape a counter cannot have.
/// </para>
/// </remarks>
public static class StackedAreaChart {

    private const double LabelHeight = 11 * SvgText.TypeScale;
    private const double FillOpacity = 0.35;

    /// <summary>Renders one stacked area chart.</summary>
    /// <param name="series">The parts; they are stacked in label order, not in the order given.</param>
    /// <param name="x">The time axis; tick positions are seconds after the first sample.</param>
    /// <param name="y">The value axis; its title is drawn and its ticks are ignored, because the range
    /// is the sum of the bands and is chosen here.</param>
    /// <param name="size">The full drawing area, labels included.</param>
    /// <param name="frame">The drawing's title, axis title and words for no data.</param>
    /// <returns>One <c>&lt;svg&gt;</c> element, with every label escaped.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size leaves no room to plot in.</exception>
    /// <exception cref="ArgumentException">A value is negative, which a stack cannot draw.</exception>
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

        IReadOnlyList<ChartSeries> ordered = [.. series.OrderBy(line => line.Label, StringComparer.Ordinal)];

        StringBuilder svg = new();
        ChartPlot plot = ChartChrome.BeginPlot(svg, size, "stacked", frame, ChartChrome.Describe(x, y),
            [.. ordered.Select(line => line.Label)], out IReadOnlyList<LegendEntry> legend);

        IReadOnlyList<ChartPoint> timeline = Timeline(ordered);
        double[][] values = [.. ordered.Select(line => ValuesAt(line, timeline))];
        double max = timeline.Count == 0 ? 0 : Totals(values, timeline.Count).Max();

        // The axis is the sum, not any one series, so the ticks are chosen here rather than passed in.
        // An empty stack still gets a scale, which is what keeps its grid on the page.
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(0, max > 0 ? max : 1);
        double axisMax = Math.Max(max > 0 ? max : 1, ticks.Count == 0 ? 1 : ticks.Max(tick => tick.Position));
        double sampleSpan = timeline.Count < 2 ? 0 : (timeline[^1].At - timeline[0].At).TotalSeconds;
        double span = ChartSamples.TimeSpan(sampleSpan, x.Ticks);

        ChartChrome.WriteAxisTitle(svg, y.Title, plot);
        ChartChrome.WriteValueGrid(svg, ticks, plot, tick => plot.Value(tick, 0, axisMax));
        ChartChrome.WriteTimeGrid(svg, x.Ticks, plot, size, tick => plot.Time(tick, span));
        ChartChrome.Border(svg, plot);

        if (max <= 0) {
            ChartChrome.Empty(svg, plot, frame.EmptyText);
            svg.Append("</svg>\n");

            return svg.ToString();
        }

        double[] below = new double[timeline.Count];

        for (int index = 0; index < ordered.Count; index++) {
            ChartSeries line = ordered[index];
            string colour = SvgText.SeriesStroke(index);
            string dash = SvgText.SeriesDash(index) is { } pattern
                ? " stroke-dasharray=\"" + pattern + "\""
                : string.Empty;

            double[] tops = new double[timeline.Count];
            for (int column = 0; column < timeline.Count; column++) {
                tops[column] = below[column] + values[index][column];
            }

            ChartChrome.BeginSeries(svg, frame, index, line.Label, legend[index]);
            svg.Append("<path class=\"series area ").Append(SvgText.SeriesColour(index))
                .Append("\" d=\"").Append(Area(timeline, below, tops, span, plot, axisMax))
                .Append("\" fill=\"").Append(colour).Append("\" fill-opacity=\"")
                .Append(SvgText.Number(FillOpacity)).Append("\" stroke=\"none\"/>\n");
            svg.Append("<polyline class=\"series ").Append(SvgText.SeriesColour(index))
                .Append("\" points=\"").Append(Top(timeline, tops, span, plot, axisMax))
                .Append("\" fill=\"none\" stroke=\"").Append(colour)
                .Append("\" stroke-width=\"2\"").Append(dash).Append("/>\n");
            ChartChrome.EndSeries(svg);

            WriteEndLabel(svg, line.Label, colour, timeline, below, tops, span, plot, axisMax);
            below = tops;
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static void WriteEndLabel(
        StringBuilder svg,
        string label,
        string colour,
        IReadOnlyList<ChartPoint> timeline,
        double[] below,
        double[] tops,
        double span,
        ChartPlot plot,
        double axisMax) {

        if (string.IsNullOrWhiteSpace(label) || timeline.Count == 0) {
            return;
        }

        int last = timeline.Count - 1;
        double bottom = plot.Value(below[last], 0, axisMax);
        double upper = plot.Value(tops[last], 0, axisMax);
        if (bottom - upper < LabelHeight) {
            return;
        }

        double x = plot.Time((timeline[last].At - timeline[0].At).TotalSeconds, span);
        double y = (bottom + upper) / 2;

        svg.Append("<text class=\"series-label\" x=\"").Append(SvgText.Number(x - (4 * SvgText.TypeScale)))
            .Append("\" y=\"").Append(SvgText.Number(y + (4 * SvgText.TypeScale)))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.SeriesFontSize))
            .Append("\" fill=\"").Append(colour).Append("\" text-anchor=\"end\">")
            .Append(SvgText.Escape(label)).Append("</text>\n");
    }

    private static string Area(
        IReadOnlyList<ChartPoint> timeline,
        double[] below,
        double[] tops,
        double span,
        ChartPlot plot,
        double axisMax) {

        StringBuilder path = new();
        for (int column = 0; column < timeline.Count; column++) {
            double x = plot.Time((timeline[column].At - timeline[0].At).TotalSeconds, span);
            double y = plot.Value(tops[column], 0, axisMax);
            path.Append(column == 0 ? "M" : "L").Append(SvgText.Number(x)).Append(' ').Append(SvgText.Number(y)).Append(' ');
        }

        for (int column = timeline.Count - 1; column >= 0; column--) {
            double x = plot.Time((timeline[column].At - timeline[0].At).TotalSeconds, span);
            double y = plot.Value(below[column], 0, axisMax);
            path.Append('L').Append(SvgText.Number(x)).Append(' ').Append(SvgText.Number(y)).Append(' ');
        }

        return path.Append('Z').ToString();
    }

    private static string Top(
        IReadOnlyList<ChartPoint> timeline,
        double[] tops,
        double span,
        ChartPlot plot,
        double axisMax) {

        return string.Join(' ', timeline.Select((point, column) =>
            SvgText.Number(plot.Time((point.At - timeline[0].At).TotalSeconds, span)) + "," +
            SvgText.Number(plot.Value(tops[column], 0, axisMax))));
    }

    private static IReadOnlyList<ChartPoint> Timeline(IReadOnlyList<ChartSeries> series) {
        // The queries return aligned samples, so the first series' instants are the grid and every other
        // series is looked up against them.
        return series.Count == 0
            ? []
            : [.. series[0].Points.Select(point => new ChartPoint(point.At, null))];
    }

    private static double[] ValuesAt(ChartSeries series, IReadOnlyList<ChartPoint> timeline) {
        Dictionary<DateTimeOffset, double> byInstant = [];
        foreach (ChartPoint point in series.Points) {
            if (point.Value is double value) {
                if (!double.IsFinite(value) || value < 0) {
                    throw new ArgumentException(
                        $"The series '{series.Label}' has a value a stack cannot draw: {value}.",
                        nameof(series));
                }

                byInstant[point.At] = value;
            }
        }

        return [.. timeline.Select(point => byInstant.TryGetValue(point.At, out double value) ? value : 0)];
    }

    private static double[] Totals(double[][] values, int columns) {
        double[] totals = new double[columns];
        foreach (double[] series in values) {
            for (int column = 0; column < columns; column++) {
                totals[column] += series[column];
            }
        }

        return totals;
    }
}
