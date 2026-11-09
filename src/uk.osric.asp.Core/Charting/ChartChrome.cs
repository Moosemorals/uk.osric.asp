// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Globalization;
using System.Text;

/// <summary>The title and legend band every chart draws for itself.</summary>
/// <remarks>
/// One place owns the band, so the three renderers cannot disagree about where the plot starts or how a
/// legend is laid out. The layout is deliberately arithmetic rather than measured: the estimate is a
/// fixed width per character, a row wraps when it would leave the drawing, and a test can pin the
/// result.
/// </remarks>
internal static class ChartChrome {

    /// <summary>The height the title band takes.</summary>
    internal const double TitleHeight = 20;

    /// <summary>The height of one legend row.</summary>
    internal const double LegendRowHeight = 14;

    /// <summary>The size of the axis and legend labels.</summary>
    internal const double FontSize = 11;

    /// <summary>The size of the title.</summary>
    internal const double TitleFontSize = 12;

    internal const double LeftPad = 6;
    internal const double RightPad = 6;
    internal const double KeyWidth = 14;
    internal const double KeyGap = 4;
    internal const double ItemGap = 12;

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

        svg.Append("<svg class=\"").Append(cssClass).Append("\" viewBox=\"0 0 ")
            .Append(SvgText.Number(size.Width)).Append(' ').Append(SvgText.Number(size.Height))
            .Append("\" role=\"img\">\n");
        svg.Append("<title>").Append(SvgText.Escape(frame.Title)).Append("</title>\n");
        svg.Append("<desc>").Append(SvgText.Escape(description)).Append("</desc>\n");

        // The title is what a reader reaches for, so it is the link that opens the chart on its own.
        if (frame.TitleHref is { Length: > 0 } href) {
            svg.Append("<a class=\"chart-title-link\" href=\"").Append(SvgText.Escape(href)).Append("\">");
        }

        svg.Append("<text class=\"chart-title\" x=\"").Append(SvgText.Number(LeftPad))
            .Append("\" y=\"").Append(SvgText.Number(TitleFontSize + 2))
            .Append("\" font-size=\"").Append(SvgText.Number(TitleFontSize))
            .Append("\" fill=\"#222222\" text-anchor=\"start\">").Append(SvgText.Escape(frame.Title))
            .Append("</text>");

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
    internal static IReadOnlyList<LegendEntry> LayoutLegend(
        ChartSize size,
        double top,
        IReadOnlyList<string> labels,
        out double plotTop) {

        ArgumentNullException.ThrowIfNull(labels);

        List<LegendEntry> entries = [];
        double x = LeftPad;
        double y = top;

        for (int index = 0; index < labels.Count; index++) {
            double width = KeyWidth + KeyGap + SvgText.EstimateWidth(labels[index]) + ItemGap;
            if (x > LeftPad && x + width > size.Width - RightPad) {
                x = LeftPad;
                y += LegendRowHeight;
            }

            entries.Add(new LegendEntry(index, x, y));
            x += width;
        }

        plotTop = labels.Count == 0 ? top : y + LegendRowHeight + 2;

        return entries;
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
            .Append("\" y1=\"").Append(SvgText.Number(entry.Y + 4))
            .Append("\" x2=\"").Append(SvgText.Number(entry.X + KeyWidth))
            .Append("\" y2=\"").Append(SvgText.Number(entry.Y + 4))
            .Append("\" stroke=\"").Append(SvgText.SeriesStroke(entry.Index))
            .Append("\" stroke-width=\"2\"").Append(dash).Append("/>\n");
        svg.Append("<text class=\"chart-legend-label\" x=\"").Append(SvgText.Number(entry.X + KeyWidth + KeyGap))
            .Append("\" y=\"").Append(SvgText.Number(entry.Y + 8))
            .Append("\" font-size=\"").Append(SvgText.Number(FontSize))
            .Append("\" fill=\"#444444\" text-anchor=\"start\">").Append(SvgText.Escape(label))
            .Append("</text>\n");
    }

    /// <summary>Writes the empty state in place of a plot.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="size">The full drawing area.</param>
    /// <param name="top">The foot of the title band.</param>
    /// <param name="message">What to say.</param>
    internal static void Empty(StringBuilder svg, ChartSize size, double top, string message) {
        ArgumentNullException.ThrowIfNull(svg);

        double baseline = top + ((size.Height - top) / 2);

        svg.Append("<text class=\"chart-empty\" x=\"").Append(SvgText.Number(size.Width / 2))
            .Append("\" y=\"").Append(SvgText.Number(baseline))
            .Append("\" font-size=\"").Append(SvgText.Number(FontSize))
            .Append("\" fill=\"#666666\" text-anchor=\"middle\">").Append(SvgText.Escape(message))
            .Append("</text>\n");
    }

    /// <summary>Draws the plot's border: the axes at the edges of the drawn area.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="left">The plot's left edge.</param>
    /// <param name="top">The plot's top edge.</param>
    /// <param name="width">The plot's width.</param>
    /// <param name="height">The plot's height.</param>
    /// <remarks>
    /// Drawn after the grid and before the series, so it is a frame rather than a line across the data.
    /// </remarks>
    internal static void Border(StringBuilder svg, double left, double top, double width, double height) {
        ArgumentNullException.ThrowIfNull(svg);

        svg.Append("<rect class=\"chart-frame\" x=\"").Append(SvgText.Number(left))
            .Append("\" y=\"").Append(SvgText.Number(top))
            .Append("\" width=\"").Append(SvgText.Number(width))
            .Append("\" height=\"").Append(SvgText.Number(height))
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
