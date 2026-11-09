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

    private const double LeftPad = 6;
    private const double RightPad = 6;
    private const double KeyWidth = 14;
    private const double KeyGap = 4;
    private const double ItemGap = 12;

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
        svg.Append("<text class=\"chart-title\" x=\"").Append(SvgText.Number(LeftPad))
            .Append("\" y=\"").Append(SvgText.Number(TitleFontSize + 2))
            .Append("\" font-size=\"").Append(SvgText.Number(TitleFontSize))
            .Append("\" fill=\"#222222\" text-anchor=\"start\">").Append(SvgText.Escape(frame.Title))
            .Append("</text>\n");

        return TitleHeight;
    }

    /// <summary>Writes a swatch, a dash sample and a label for each series, and returns the plot's top.</summary>
    /// <param name="svg">The drawing being written.</param>
    /// <param name="size">The full drawing area.</param>
    /// <param name="top">The band's foot, from <see cref="Begin"/>.</param>
    /// <param name="labels">The series labels, in series order.</param>
    internal static double Legend(StringBuilder svg, ChartSize size, double top, IReadOnlyList<string> labels) {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(labels);

        if (labels.Count == 0) {
            return top;
        }

        double x = LeftPad;
        double y = top;

        for (int index = 0; index < labels.Count; index++) {
            double width = KeyWidth + KeyGap + SvgText.EstimateWidth(labels[index]) + ItemGap;
            if (x > LeftPad && x + width > size.Width - RightPad) {
                x = LeftPad;
                y += LegendRowHeight;
            }

            string dash = SvgText.SeriesDash(index) is { } pattern
                ? " stroke-dasharray=\"" + pattern + "\""
                : string.Empty;

            svg.Append("<line class=\"chart-legend-key ").Append(SvgText.SeriesColour(index))
                .Append("\" x1=\"").Append(SvgText.Number(x))
                .Append("\" y1=\"").Append(SvgText.Number(y + 4))
                .Append("\" x2=\"").Append(SvgText.Number(x + KeyWidth))
                .Append("\" y2=\"").Append(SvgText.Number(y + 4))
                .Append("\" stroke=\"").Append(SvgText.SeriesStroke(index))
                .Append("\" stroke-width=\"2\"").Append(dash).Append("/>\n");
            svg.Append("<text class=\"chart-legend-label\" x=\"").Append(SvgText.Number(x + KeyWidth + KeyGap))
                .Append("\" y=\"").Append(SvgText.Number(y + 8))
                .Append("\" font-size=\"").Append(SvgText.Number(FontSize))
                .Append("\" fill=\"#444444\" text-anchor=\"start\">").Append(SvgText.Escape(labels[index]))
                .Append("</text>\n");

            x += width;
        }

        return y + LegendRowHeight + 2;
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
