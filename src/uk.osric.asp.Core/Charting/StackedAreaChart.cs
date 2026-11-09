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

    private const double LeftMargin = 40;
    private const double RightMargin = 12;
    private const double BottomMargin = 22;
    private const double LabelHeight = 11;
    private const double FillOpacity = 0.35;

    /// <summary>Renders one stacked area chart.</summary>
    /// <param name="series">The parts; they are stacked in label order, not in the order given.</param>
    /// <param name="x">The time axis; tick positions are seconds after the first sample.</param>
    /// <param name="y">The value axis title and ticks; the range is taken from the totals.</param>
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

        double plotWidth = size.Width - LeftMargin - RightMargin;
        if (plotWidth <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The chart is too small for its labels.");
        }

        IReadOnlyList<ChartSeries> ordered = [.. series.OrderBy(line => line.Label, StringComparer.Ordinal)];

        StringBuilder svg = new();
        double top = ChartChrome.Begin(svg, size, "stacked", frame, ChartChrome.Describe(x, y));
        top = ChartChrome.Legend(svg, size, top, [.. ordered.Select(line => line.Label)]);

        double plotHeight = size.Height - top - BottomMargin;
        if (plotHeight <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The chart is too small for its labels.");
        }

        WriteAxisTitle(svg, y.Title, top, plotHeight);

        IReadOnlyList<ChartPoint> timeline = Timeline(ordered);
        if (timeline.Count == 0) {
            ChartChrome.Empty(svg, size, top, frame.EmptyText);
            svg.Append("</svg>\n");

            return svg.ToString();
        }

        double[][] values = [.. ordered.Select(line => ValuesAt(line, timeline))];
        double[] totals = Totals(values, timeline.Count);
        double max = totals.Max();
        if (max <= 0) {
            ChartChrome.Empty(svg, size, top, frame.EmptyText);
            svg.Append("</svg>\n");

            return svg.ToString();
        }

        // The axis is the sum, not any one series, so the ticks are chosen here rather than passed in.
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(0, max);
        double axisMax = Math.Max(max, ticks.Max(tick => tick.Position));
        double span = Math.Max((timeline[^1].At - timeline[0].At).TotalSeconds, 1);

        WriteGrid(svg, x, ticks, top, plotHeight, plotWidth, size, span, axisMax);

        double[] below = new double[timeline.Count];
        double right = LeftMargin + plotWidth;

        for (int index = 0; index < ordered.Count; index++) {
            ChartSeries line = ordered[index];
            string colour = SvgText.SeriesStroke(index);
            string dash = SvgText.SeriesDash(index) is { } pattern
                ? " stroke-dasharray=\"" + pattern + "\""
                : string.Empty;
            string id = frame.Id + "-s" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

            double[] tops = new double[timeline.Count];
            for (int column = 0; column < timeline.Count; column++) {
                tops[column] = below[column] + values[index][column];
            }

            svg.Append("<a class=\"series-link\" id=\"").Append(SvgText.Escape(id))
                .Append("\" href=\"#").Append(SvgText.Escape(id)).Append("\" tabindex=\"-1\">\n");
            svg.Append("<title>").Append(SvgText.Escape(line.Label)).Append("</title>\n");
            svg.Append("<path class=\"series area ").Append(SvgText.SeriesColour(index))
                .Append("\" d=\"").Append(Area(timeline, below, tops, span, plotWidth, top, plotHeight, axisMax))
                .Append("\" fill=\"").Append(colour).Append("\" fill-opacity=\"")
                .Append(SvgText.Number(FillOpacity)).Append("\" stroke=\"none\"/>\n");
            svg.Append("<polyline class=\"series ").Append(SvgText.SeriesColour(index))
                .Append("\" points=\"").Append(Top(timeline, tops, span, plotWidth, top, plotHeight, axisMax))
                .Append("\" fill=\"none\" stroke=\"").Append(colour)
                .Append("\" stroke-width=\"2\"").Append(dash).Append("/>\n");
            svg.Append("</a>\n");

            WriteEndLabel(svg, line.Label, colour, timeline, below, tops, span, plotWidth, top, plotHeight, axisMax, right);
            below = tops;
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static void WriteGrid(
        StringBuilder svg,
        ChartAxis x,
        IReadOnlyList<ChartTick> ticks,
        double top,
        double plotHeight,
        double plotWidth,
        ChartSize size,
        double span,
        double axisMax) {

        foreach (ChartTick tick in ticks.OrderBy(tick => tick.Position)) {
            double at = top + ((1 - (tick.Position / axisMax)) * plotHeight);
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

        foreach (ChartTick tick in x.Ticks.OrderBy(tick => tick.Position)) {
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
    }

    private static void WriteAxisTitle(StringBuilder svg, string title, double top, double plotHeight) {
        double middle = top + (plotHeight / 2);

        svg.Append("<text class=\"chart-axis-title\" transform=\"rotate(-90 10 ").Append(SvgText.Number(middle))
            .Append(")\" x=\"10\" y=\"").Append(SvgText.Number(middle))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize))
            .Append("\" fill=\"#666666\" text-anchor=\"middle\">").Append(SvgText.Escape(title))
            .Append("</text>\n");
    }

    private static void WriteEndLabel(
        StringBuilder svg,
        string label,
        string colour,
        IReadOnlyList<ChartPoint> timeline,
        double[] below,
        double[] tops,
        double span,
        double plotWidth,
        double top,
        double plotHeight,
        double axisMax,
        double right) {

        if (string.IsNullOrWhiteSpace(label) || timeline.Count == 0) {
            return;
        }

        int last = timeline.Count - 1;
        double bottom = Position(below[last], top, plotHeight, axisMax);
        double upper = Position(tops[last], top, plotHeight, axisMax);
        if (bottom - upper < LabelHeight) {
            return;
        }

        double x = Math.Min(right, LeftMargin + (((timeline[last].At - timeline[0].At).TotalSeconds / span) * plotWidth));
        double y = (bottom + upper) / 2;

        svg.Append("<text class=\"series-label\" x=\"").Append(SvgText.Number(x - 4))
            .Append("\" y=\"").Append(SvgText.Number(y + 4))
            .Append("\" font-size=\"").Append(SvgText.Number(ChartChrome.FontSize - 1))
            .Append("\" fill=\"").Append(colour).Append("\" text-anchor=\"end\">")
            .Append(SvgText.Escape(label)).Append("</text>\n");
    }

    private static string Area(
        IReadOnlyList<ChartPoint> timeline,
        double[] below,
        double[] tops,
        double span,
        double plotWidth,
        double top,
        double plotHeight,
        double axisMax) {

        StringBuilder path = new();
        for (int column = 0; column < timeline.Count; column++) {
            double x = LeftMargin + (((timeline[column].At - timeline[0].At).TotalSeconds / span) * plotWidth);
            double y = Position(tops[column], top, plotHeight, axisMax);
            path.Append(column == 0 ? "M" : "L").Append(SvgText.Number(x)).Append(' ').Append(SvgText.Number(y)).Append(' ');
        }

        for (int column = timeline.Count - 1; column >= 0; column--) {
            double x = LeftMargin + (((timeline[column].At - timeline[0].At).TotalSeconds / span) * plotWidth);
            double y = Position(below[column], top, plotHeight, axisMax);
            path.Append('L').Append(SvgText.Number(x)).Append(' ').Append(SvgText.Number(y)).Append(' ');
        }

        return path.Append('Z').ToString();
    }

    private static string Top(
        IReadOnlyList<ChartPoint> timeline,
        double[] tops,
        double span,
        double plotWidth,
        double top,
        double plotHeight,
        double axisMax) {

        return string.Join(' ', timeline.Select((point, column) =>
            SvgText.Number(LeftMargin + (((point.At - timeline[0].At).TotalSeconds / span) * plotWidth)) + "," +
            SvgText.Number(Position(tops[column], top, plotHeight, axisMax))));
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

    private static double Position(double value, double top, double plotHeight, double axisMax) =>
        top + ((1 - (value / axisMax)) * plotHeight);
}
