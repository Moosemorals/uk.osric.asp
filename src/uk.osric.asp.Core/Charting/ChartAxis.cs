// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>One axis of a chart: a title and the ticks drawn along it.</summary>
/// <param name="Title">What the axis measures, for the chart's description.</param>
/// <param name="Ticks">The ticks, in any order; the chart sorts them by position.</param>
/// <param name="Divisor">
/// What the samples are divided by to reach the ticks' units — 1000 for an axis of kilobytes. The
/// values a chart is given stay in the unit they were recorded in, so an axis that rescales its labels
/// has to say how far it rescaled them rather than leave the plot in a different unit from its labels.
/// </param>
public sealed record ChartAxis(string Title, IReadOnlyList<ChartTick> Ticks, double Divisor = 1) {

    /// <summary>Rescales a sample into the axis' unit, keeping a gap a gap.</summary>
    /// <param name="value">The sample, in the unit it was recorded in.</param>
    /// <returns>The sample in the ticks' unit, or null.</returns>
    internal double? Scale(double? value) =>
        value is double sample && Divisor != 1 ? sample / Divisor : value;
}
