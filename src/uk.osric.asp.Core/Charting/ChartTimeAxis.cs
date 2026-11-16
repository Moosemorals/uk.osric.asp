// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>A chart's time axis: the window it covers, and the ticks drawn along it.</summary>
/// <remarks>
/// The window is part of the axis rather than something a chart infers from its samples, so the grid,
/// the marks and the labels are all measured from one origin. A sample sits at its fraction of the
/// window, which is what puts a line whose data starts late — or stops early — where it belongs
/// instead of at the plot's left edge.
/// </remarks>
/// <param name="Title">What the axis measures, for the chart's description.</param>
/// <param name="From">The instant at the axis' left edge.</param>
/// <param name="To">The instant at its right edge.</param>
/// <param name="Ticks">The ticks, positioned in seconds after <paramref name="From"/>.</param>
public sealed record ChartTimeAxis(
    string Title,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ChartTick> Ticks) {

    /// <summary>How far an instant is into the window, in seconds after its start.</summary>
    /// <param name="at">The instant to place.</param>
    internal double Offset(DateTimeOffset at) => (at - From).TotalSeconds;

    /// <summary>The window's width in seconds, floored at one so a zero-width window cannot divide by zero.</summary>
    internal double Span => Math.Max((To - From).TotalSeconds, 1);

    /// <summary>How far the window reaches.</summary>
    internal TimeSpan Duration => To - From;
}
