// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>The drawing area of a chart, in SVG user units.</summary>
/// <param name="Width">The full width, including the space the labels need.</param>
/// <param name="Height">The full height, including the space the labels need.</param>
public readonly record struct ChartSize(double Width, double Height) {

    /// <summary>The width of the plot itself, which is what a tick label has room to use.</summary>
    public double PlotWidth => Width - ChartChrome.LeftMargin - ChartChrome.RightMargin;
}
