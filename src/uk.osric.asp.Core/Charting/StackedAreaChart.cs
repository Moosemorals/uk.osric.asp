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
    /// <param name="x">The time axis: the window the plot covers and its ticks, positioned in seconds
    /// after the window's start.</param>
    /// <param name="y">The value axis; its title is drawn and its ticks are ignored, because the range
    /// is the sum of the bands and is chosen here.</param>
    /// <param name="size">The full drawing area, labels included.</param>
    /// <param name="frame">The drawing's title, axis title and words for no data.</param>
    /// <returns>One <c>&lt;svg&gt;</c> element, with every label escaped.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size leaves no room to plot in.</exception>
    /// <exception cref="ArgumentException">A value is negative, which a stack cannot draw.</exception>
    public static string Render(
        this IReadOnlyList<ChartSeries> series,
        ChartTimeAxis x,
        ChartAxis y,
        ChartSize size,
        ChartFrame frame) {

        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(frame);

        IReadOnlyList<ChartSeries> ordered = [.. series.OrderBy(line => line.Label, StringComparer.Ordinal)];

        StringBuilder svg = new();
        ChartPlot plot = svg.BeginPlot(size, "stacked", frame, x.Describe(y),
            [.. ordered.Select(line => line.Label)], out IReadOnlyList<LegendEntry> legend);

        IReadOnlyList<ChartPoint> timeline = Timeline(ordered);
        double[][] values = [.. ordered.Select(line => ValuesAt(line, timeline))];
        double max = timeline.Count == 0 ? 0 : Totals(values, timeline.Count).Max();

        // The axis is the sum, not any one series, so the ticks are chosen here rather than passed in.
        // An empty stack still gets a scale, which is what keeps its grid on the page.
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(0, max > 0 ? max : 1);
        double axisMax = Math.Max(max > 0 ? max : 1, ticks.Count == 0 ? 1 : ticks.Max(tick => tick.Position));

        svg.WriteAxisTitle(y.Title, plot);
        svg.WriteValueGrid(ticks, plot, tick => plot.Value(tick, 0, axisMax));
        svg.WriteTimeGrid(x, plot, size);
        svg.Border(plot);

        if (max <= 0) {
            svg.Empty(plot, frame.EmptyText);
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

            List<(double X, double Below, double Top)> band = Visible(timeline, below, tops, x, plot, axisMax);

            svg.BeginSeries(frame, index, line.Label, legend[index]);
            if (band.Count > 0) {
                svg.Append("<path class=\"series area ").Append(SvgText.SeriesColour(index))
                    .Append("\" d=\"").Append(Area(band))
                    .Append("\" fill=\"").Append(colour).Append("\" fill-opacity=\"")
                    .Append(SvgText.Number(FillOpacity)).Append("\" stroke=\"none\"/>\n");
                svg.Append("<polyline class=\"series ").Append(SvgText.SeriesColour(index))
                    .Append("\" points=\"").Append(Top(band))
                    .Append("\" fill=\"none\" stroke=\"").Append(colour)
                    .Append("\" stroke-width=\"2\"").Append(dash).Append("/>\n");
            }

            svg.EndSeries();

            WriteEndLabel(svg, line.Label, colour, band);
            below = tops;
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static void WriteEndLabel(
        StringBuilder svg,
        string label,
        string colour,
        IReadOnlyList<(double X, double Below, double Top)> band) {

        if (string.IsNullOrWhiteSpace(label) || band.Count == 0) {
            return;
        }

        (double x, double bottom, double upper) = band[^1];
        if (bottom - upper < LabelHeight) {
            return;
        }

        double y = (bottom + upper) / 2;

        svg.Append("<text class=\"series-label\" x=\"").Append(SvgText.Number(x - (4 * SvgText.TypeScale)))
            .Append("\" y=\"").Append(SvgText.Number(y + (4 * SvgText.TypeScale)))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.SeriesFontSize))
            .Append("\" fill=\"").Append(colour).Append("\" text-anchor=\"end\">")
            .Append(label.Escape()).Append("</text>\n");
    }

    // Where each column of the band sits in the plot, cut to the window so an overshoot ends at the
    // edge rather than running along it.
    private static List<(double X, double Below, double Top)> Visible(
        IReadOnlyList<ChartPoint> timeline,
        double[] below,
        double[] tops,
        ChartTimeAxis axis,
        ChartPlot plot,
        double axisMax) {

        List<(double X, double Below, double Top)> columns = [];
        for (int column = 0; column < timeline.Count; column++) {
            columns.Add((
                plot.Time(axis, axis.Offset(timeline[column].At)),
                plot.Value(below[column], 0, axisMax),
                plot.Value(tops[column], 0, axisMax)));
        }

        return ChartClip.Stack(columns, plot);
    }

    private static string Area(IReadOnlyList<(double X, double Below, double Top)> band) {
        StringBuilder path = new();
        for (int column = 0; column < band.Count; column++) {
            path.Append(column == 0 ? "M" : "L").Append(SvgText.Number(band[column].X)).Append(' ')
                .Append(SvgText.Number(band[column].Top)).Append(' ');
        }

        for (int column = band.Count - 1; column >= 0; column--) {
            path.Append('L').Append(SvgText.Number(band[column].X)).Append(' ')
                .Append(SvgText.Number(band[column].Below)).Append(' ');
        }

        return path.Append('Z').ToString();
    }

    private static string Top(IReadOnlyList<(double X, double Below, double Top)> band) =>
        string.Join(' ', band.Select(column => SvgText.Number(column.X) + "," + SvgText.Number(column.Top)));

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
