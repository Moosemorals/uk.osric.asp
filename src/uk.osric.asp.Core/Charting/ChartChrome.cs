// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Globalization;
using System.Text;
using uk.osric.asp.Core.Assistant;

/// <summary>The band, the axes and the frame every chart draws for itself.</summary>
/// <remarks>
/// One place owns the geometry, so the three renderers cannot disagree about where the plot starts,
/// how a legend is laid out, or what an empty chart still shows. The layout is deliberately arithmetic
/// rather than measured: the estimate is a fixed width per character, a row wraps when it would leave
/// the drawing, and a test can pin the result.
/// </remarks>
internal static class ChartChrome {

    /// <summary>The height the title band takes.</summary>
    internal const double TitleHeight = 20 * SvgText.TypeScale;

    /// <summary>The height of one legend row.</summary>
    internal const double LegendRowHeight = 14 * SvgText.TypeScale;

    /// <summary>The size of the axis and legend labels.</summary>
    internal const double FontSize = 11 * SvgText.TypeScale;

    /// <summary>The size of the title.</summary>
    internal const double TitleFontSize = 12 * SvgText.TypeScale;

    /// <summary>The size of the label at the end of a series' line, a shade smaller than the ticks.</summary>
    internal const double SeriesFontSize = 10 * SvgText.TypeScale;

    internal const double KeyWidth = 14;
    internal const double KeyGap = 4;
    internal const double ItemGap = 12;

    /// <summary>
    /// Where every plot starts. The title and the first legend entry line up with it, so the drawing
    /// reads as one column rather than a label band beside a chart.
    /// </summary>
    internal const double LeftMargin = 60 * SvgText.TypeScale;

    /// <summary>How much room the right edge keeps clear of the drawing.</summary>
    internal const double RightMargin = 12;

    /// <summary>How much room the time labels take below the plot.</summary>
    internal const double BottomMargin = 24 * SvgText.TypeScale;

    /// <summary>Writes the opening element, the names and the visible title, and returns the band's foot.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="size">The full drawing area.</param>
    /// <param name="cssClass">The drawing's class: <c>chart</c>, <c>heatmap</c> or <c>stacked</c>.</param>
    /// <param name="frame">The chart's own words.</param>
    /// <param name="description">The longer description an assistive technology reads.</param>
    internal static double Begin(StringBuilder svg, ChartSize size, string cssClass, ChartFrame frame, string description) {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(frame);
        frame.Validate();

        // A frame that explains itself says the same thing to a screen reader and to a mouse: the
        // catalogue's sentence replaces the generated axis summary rather than sitting beside it.
        string explained = frame.Description is { Length: > 0 } ? frame.Description : description;

        // The namespace is implied inside an HTML page and required in a standalone document: without
        // it a browser opening /status/chart on its own shows the markup as a tree, not as a drawing.
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"").Append(cssClass).Append("\" viewBox=\"0 0 ")
            .Append(SvgText.Number(size.Width)).Append(' ').Append(SvgText.Number(size.Height))
            .Append("\" role=\"img\">\n");
        svg.Append("<title>").Append(SvgText.Escape(frame.Title)).Append("</title>\n");
        svg.Append("<desc>").Append(SvgText.Escape(explained)).Append("</desc>\n");

        // The title is what a reader reaches for, so it is the link that opens the chart on its own.
        if (frame.TitleHref is { Length: > 0 } href) {
            svg.Append("<a class=\"chart-title-link\" href=\"").Append(SvgText.Escape(href)).Append("\">");
        }

        svg.Append("<text class=\"chart-title\" x=\"").Append(SvgText.Number(LeftMargin))
            .Append("\" y=\"").Append(SvgText.Number(TitleFontSize + (2 * SvgText.TypeScale)))
            .Append("\" font-size=\"").Append(SvgText.Number(TitleFontSize))
            .Append("\" fill=\"#222222\" text-anchor=\"start\">");

        // A title element inside the text is what a browser shows as a tooltip when the title is
        // hovered; it also gives the link a name that says what the chart is for.
        if (frame.Description is { Length: > 0 } hover) {
            svg.Append("<title>").Append(SvgText.Escape(hover)).Append("</title>");
        }

        svg.Append(SvgText.Escape(frame.Title)).Append("</text>");

        if (frame.TitleHref is { Length: > 0 }) {
            svg.Append("</a>");
        }

        svg.Append('\n');

        return TitleHeight;
    }

    /// <summary>Lays out a legend band and returns the plot's top.</summary>
    /// <param name="size">The full drawing area.</param>
    /// <param name="top">The band's foot, from <see cref="Begin"/>.</param>
    /// <param name="labels">The series labels, in series order.</param>
    /// <param name="plotTop">Where the plot starts once the legend has wrapped.</param>
    private static IReadOnlyList<LegendEntry> LayoutLegend(
        ChartSize size,
        double top,
        IReadOnlyList<string> labels,
        out double plotTop) {

        ArgumentNullException.ThrowIfNull(labels);

        List<LegendEntry> entries = [];
        double x = LeftMargin;
        double y = top;

        for (int index = 0; index < labels.Count; index++) {
            double width = KeyWidth + KeyGap + SvgText.EstimateWidth(labels[index]) + ItemGap;
            if (x > LeftMargin && x + width > size.Width - RightMargin) {
                x = LeftMargin;
                y += LegendRowHeight;
            }

            entries.Add(new LegendEntry(index, x, y));
            x += width;
        }

        plotTop = labels.Count == 0 ? top : y + LegendRowHeight + 2;

        return entries;
    }

    /// <summary>Writes the band and its legend, and works out the plot they leave.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="size">The full drawing area.</param>
    /// <param name="cssClass">The drawing's class: <c>chart</c>, <c>heatmap</c> or <c>stacked</c>.</param>
    /// <param name="frame">The chart's own words.</param>
    /// <param name="description">The longer description an assistive technology reads.</param>
    /// <param name="labels">The series labels, in series order.</param>
    /// <param name="legend">Where each legend entry sits.</param>
    /// <returns>Where the plot goes.</returns>
    internal static ChartPlot BeginPlot(
        StringBuilder svg,
        ChartSize size,
        string cssClass,
        ChartFrame frame,
        string description,
        IReadOnlyList<string> labels,
        out IReadOnlyList<LegendEntry> legend) {

        double top = Begin(svg, size, cssClass, frame, description);
        legend = LayoutLegend(size, top, labels, out double plotTop);

        return Plot(size, plotTop);
    }

    /// <summary>Works out where a plot goes below a band.</summary>
    /// <param name="size">The full drawing area.</param>
    /// <param name="top">Where the band ends and the plot may start.</param>
    /// <returns>The plot's rectangle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size leaves no room to plot in.</exception>
    internal static ChartPlot Plot(ChartSize size, double top) {
        double width = size.Width - LeftMargin - RightMargin;
        double height = size.Height - top - BottomMargin;

        if (width <= 0 || height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The chart is too small for its labels.");
        }

        return new ChartPlot(LeftMargin, top, width, height);
    }

    /// <summary>Writes one legend entry: the series' dash sample and its label.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="entry">Where the entry goes.</param>
    /// <param name="label">The series label.</param>
    /// <remarks>
    /// The caller writes this inside the series' own anchor, so hovering or clicking the legend entry
    /// has the same effect as hovering or clicking the line it names.
    /// </remarks>
    internal static void WriteLegendItem(StringBuilder svg, LegendEntry entry, string label) {
        ArgumentNullException.ThrowIfNull(svg);

        string dash = SvgText.SeriesDash(entry.Index) is { } pattern
            ? " stroke-dasharray=\"" + pattern + "\""
            : string.Empty;

        svg.Append("<line class=\"chart-legend-key ").Append(SvgText.SeriesColour(entry.Index))
            .Append("\" x1=\"").Append(SvgText.Number(entry.X))
            .Append("\" y1=\"").Append(SvgText.Number(entry.Y + (4 * SvgText.TypeScale)))
            .Append("\" x2=\"").Append(SvgText.Number(entry.X + KeyWidth))
            .Append("\" y2=\"").Append(SvgText.Number(entry.Y + (4 * SvgText.TypeScale)))
            .Append("\" stroke=\"").Append(SvgText.SeriesStroke(entry.Index))
            .Append("\" stroke-width=\"2\"").Append(dash).Append("/>\n");
        svg.Append("<text class=\"chart-legend-label\" x=\"").Append(SvgText.Number(entry.X + KeyWidth + KeyGap))
            .Append("\" y=\"").Append(SvgText.Number(entry.Y + (8 * SvgText.TypeScale)))
            .Append("\" font-size=\"").Append(SvgText.Number(FontSize))
            .Append("\" fill=\"#444444\" text-anchor=\"start\">").Append(SvgText.Escape(label))
            .Append("</text>\n");
    }

    /// <summary>Writes the value axis title, rotated, beside the plot.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="title">What the axis measures.</param>
    /// <param name="plot">The plot it names.</param>
    internal static void WriteAxisTitle(StringBuilder svg, string title, ChartPlot plot) {
        ArgumentNullException.ThrowIfNull(svg);

        double middle = plot.Top + (plot.Height / 2);
        double x = 12 * SvgText.TypeScale;

        // The unit belongs on the axis, not only in the description: a chart of unknown units is a
        // chart nobody can act on.
        svg.Append("<text class=\"chart-axis-title\" transform=\"rotate(-90 ").Append(SvgText.Number(x))
            .Append(' ').Append(SvgText.Number(middle))
            .Append(")\" x=\"").Append(SvgText.Number(x))
            .Append("\" y=\"").Append(SvgText.Number(middle))
            .Append("\" font-size=\"").Append(SvgText.Number(FontSize))
            .Append("\" fill=\"#666666\" text-anchor=\"middle\">").Append(SvgText.Escape(title))
            .Append("</text>\n");
    }

    /// <summary>Writes the horizontal grid: one line and one value label per tick.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="ticks">The value ticks, in any order.</param>
    /// <param name="plot">The plot they cross.</param>
    /// <param name="at">Where a tick's value sits vertically.</param>
    internal static void WriteValueGrid(StringBuilder svg, IEnumerable<ChartTick> ticks, ChartPlot plot, Func<double, double> at) {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentNullException.ThrowIfNull(at);

        foreach (ChartTick tick in ticks.OrderBy(tick => tick.Position)) {
            double y = at(tick.Position);

            svg.Append("<line class=\"chart-grid\" x1=\"").Append(SvgText.Number(plot.Left))
                .Append("\" y1=\"").Append(SvgText.Number(y))
                .Append("\" x2=\"").Append(SvgText.Number(plot.Right))
                .Append("\" y2=\"").Append(SvgText.Number(y))
                .Append("\" stroke=\"#e3e3e3\" stroke-width=\"1\"/>\n");
            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(plot.Left - 4))
                .Append("\" y=\"").Append(SvgText.Number(y + (4 * SvgText.TypeScale)))
                .Append("\" font-size=\"").Append(SvgText.Number(FontSize))
                .Append("\" fill=\"#444444\" text-anchor=\"end\">").Append(SvgText.Escape(BudgetAmount.Format(tick.Position)))
                .Append("</text>\n");
        }
    }

    /// <summary>Writes the vertical grid: one line and one time label per tick.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="ticks">The time ticks.</param>
    /// <param name="plot">The plot they cross.</param>
    /// <param name="size">The full drawing area, whose floor the labels sit above.</param>
    /// <param name="at">Where a tick's position sits horizontally.</param>
    internal static void WriteTimeGrid(StringBuilder svg, IReadOnlyList<ChartTick> ticks, ChartPlot plot, ChartSize size, Func<double, double> at) {
        ArgumentNullException.ThrowIfNull(svg);

        foreach (ChartTick tick in ticks.OrderBy(tick => tick.Position)) {
            double x = at(tick.Position);

            svg.Append("<line class=\"chart-grid\" x1=\"").Append(SvgText.Number(x))
                .Append("\" y1=\"").Append(SvgText.Number(plot.Top))
                .Append("\" x2=\"").Append(SvgText.Number(x))
                .Append("\" y2=\"").Append(SvgText.Number(plot.Bottom))
                .Append("\" stroke=\"#e3e3e3\" stroke-width=\"1\"/>\n");
        }

        WriteTimeLabels(svg, ticks, plot, size, at);
    }

    /// <summary>Writes the time labels without the lines, for a plot whose cells are the grid.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="ticks">The time ticks.</param>
    /// <param name="plot">The plot they name.</param>
    /// <param name="size">The full drawing area, whose floor the labels sit above.</param>
    /// <param name="at">Where a tick's position sits horizontally.</param>
    internal static void WriteTimeLabels(StringBuilder svg, IReadOnlyList<ChartTick> ticks, ChartPlot plot, ChartSize size, Func<double, double> at) {
        ArgumentNullException.ThrowIfNull(svg);

        foreach (ChartTick tick in ticks.OrderBy(tick => tick.Position)) {
            svg.Append("<text class=\"chart-label\" x=\"").Append(SvgText.Number(at(tick.Position)))
                .Append("\" y=\"").Append(SvgText.Number(size.Height - (8 * SvgText.TypeScale)))
                .Append("\" font-size=\"").Append(SvgText.Number(FontSize))
                .Append("\" fill=\"#444444\" text-anchor=\"middle\">").Append(SvgText.Escape(tick.Label))
                .Append("</text>\n");
        }
    }

    /// <summary>Opens one series' anchor, with its hover name and its legend entry.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="frame">The chart's own words, which prefix the fragment id.</param>
    /// <param name="index">The series position, which decides the colour and the dash.</param>
    /// <param name="label">The series label.</param>
    /// <param name="legend">Where the series' legend entry sits.</param>
    /// <remarks>
    /// The caller writes the line or the band inside this anchor and closes it with
    /// <see cref="EndSeries"/>, so hovering or clicking the legend entry does what hovering or clicking
    /// the mark does.
    /// </remarks>
    internal static void BeginSeries(StringBuilder svg, ChartFrame frame, int index, string label, LegendEntry legend) {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(frame);

        string id = frame.Id + "-s" + (index + 1).ToString(CultureInfo.InvariantCulture);

        svg.Append("<a class=\"series-link ").Append(SvgText.SeriesColour(index))
            .Append("\" id=\"").Append(SvgText.Escape(id))
            .Append("\" href=\"#").Append(SvgText.Escape(id)).Append("\" tabindex=\"-1\">\n");
        svg.Append("<title>").Append(SvgText.Escape(label)).Append("</title>\n");
        WriteLegendItem(svg, legend, label);
    }

    /// <summary>Closes the anchor <see cref="BeginSeries"/> opened.</summary>
    /// <param name="svg">The drawing being written.</param>
    internal static void EndSeries(StringBuilder svg) {
        ArgumentNullException.ThrowIfNull(svg);

        svg.Append("</a>\n");
    }

    /// <summary>Writes the empty state over a plot that is already gridded and framed.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="plot">The plot that has no data.</param>
    /// <param name="message">What to say.</param>
    internal static void Empty(StringBuilder svg, ChartPlot plot, string message) {
        ArgumentNullException.ThrowIfNull(svg);

        svg.Append("<text class=\"chart-empty\" x=\"").Append(SvgText.Number(plot.Left + (plot.Width / 2)))
            .Append("\" y=\"").Append(SvgText.Number(plot.Top + (plot.Height / 2)))
            .Append("\" font-size=\"").Append(SvgText.Number(FontSize))
            .Append("\" fill=\"#666666\" text-anchor=\"middle\">").Append(SvgText.Escape(message))
            .Append("</text>\n");
    }

    /// <summary>Draws the plot's border: the axes at the edges of the drawn area.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="plot">The plot to frame.</param>
    /// <remarks>
    /// Drawn after the grid and before the series, so it is a frame rather than a line across the data.
    /// </remarks>
    internal static void Border(StringBuilder svg, ChartPlot plot) {
        ArgumentNullException.ThrowIfNull(svg);

        svg.Append("<rect class=\"chart-frame\" x=\"").Append(SvgText.Number(plot.Left))
            .Append("\" y=\"").Append(SvgText.Number(plot.Top))
            .Append("\" width=\"").Append(SvgText.Number(plot.Width))
            .Append("\" height=\"").Append(SvgText.Number(plot.Height))
            .Append("\" fill=\"none\" stroke=\"#999999\" stroke-width=\"1\" vector-effect=\"non-scaling-stroke\"/>\n");
    }

    /// <summary>Describes a time series for the drawing's <c>desc</c>.</summary>
    /// <param name="x">The time axis.</param>
    /// <param name="y">The value axis.</param>
    internal static string Describe(ChartAxis x, ChartAxis y) => $"{y.Title} over {x.Title}";

    /// <summary>Formats an instant for the hover text, in the frame's zone.</summary>
    /// <param name="at">The instant.</param>
    /// <param name="zone">The zone to write it in.</param>
    /// <param name="span">The window's duration, which decides whether the date is worth naming.</param>
    internal static string Instant(DateTimeOffset at, TimeZoneInfo zone, TimeSpan span) {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(at, zone);

        return span >= TimeSpan.FromDays(1)
            ? local.ToString("d MMM HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}

/// <summary>Where one legend entry sits.</summary>
/// <param name="Index">The series position, which decides the colour and the dash.</param>
/// <param name="X">The left edge of the key sample.</param>
/// <param name="Y">The top of the entry.</param>
internal readonly record struct LegendEntry(int Index, double X, double Y);

/// <summary>The rectangle a chart draws its data in, below the band and above the time labels.</summary>
/// <param name="Left">The value axis.</param>
/// <param name="Top">The plot's upper edge.</param>
/// <param name="Width">How wide the plot is.</param>
/// <param name="Height">How tall the plot is.</param>
internal readonly record struct ChartPlot(double Left, double Top, double Width, double Height) {

    /// <summary>The plot's right edge.</summary>
    internal double Right => Left + Width;

    /// <summary>The plot's lower edge.</summary>
    internal double Bottom => Top + Height;

    /// <summary>Where a time sits horizontally, as seconds after the plot's first sample.</summary>
    /// <param name="seconds">The offset into the window.</param>
    /// <param name="span">The width of the window, in seconds.</param>
    internal double Time(double seconds, double span) =>
        Math.Min(Right, Left + ((seconds / span) * Width));

    /// <summary>Where a value sits vertically: the higher the value, the higher the point.</summary>
    /// <param name="value">The value, in the axis' own unit.</param>
    /// <param name="min">The value at the plot's foot.</param>
    /// <param name="max">The value at its head.</param>
    internal double Value(double value, double min, double max) =>
        Top + ((1 - ((value - min) / (max - min))) * Height);
}
