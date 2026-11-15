// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>Cuts the marks a chart draws to the plot it draws them in.</summary>
/// <remarks>
/// A sample outside the window is cut at the edge rather than pinned to it: a line that stops where
/// the data stops says the data stopped, where a line running along the edge says the value did
/// something it did not. A cut segment keeps its direction, so where it crosses the edge is
/// interpolated from the two samples either side of it.
/// </remarks>
internal static class ChartClip {

    /// <summary>The part of a line that falls inside the plot.</summary>
    /// <param name="line">The line's points, in drawing order.</param>
    /// <param name="plot">The plot to cut it to.</param>
    /// <returns>The visible points, with the ends of a cut segment interpolated.</returns>
    internal static List<(double X, double Y)> Line(IReadOnlyList<(double X, double Y)> line, ChartPlot plot) {
        List<(double X, double Y)> cut = [];

        if (line.Count == 1) {
            (double x, double y) = line[0];
            if (x >= plot.Left && x <= plot.Right) {
                cut.Add((x, y));
            }

            return cut;
        }

        for (int index = 1; index < line.Count; index++) {
            (double fromX, double fromY) = line[index - 1];
            (double toX, double toY) = line[index];
            if (Span(fromX, toX, plot) is not (double from, double to)) {
                continue;
            }

            (double X, double Y) start = (Place(fromX, toX, from, plot), fromY + (from * (toY - fromY)));
            (double X, double Y) end = (Place(fromX, toX, to, plot), fromY + (to * (toY - fromY)));

            if (cut.Count == 0 || cut[^1] != start) {
                cut.Add(start);
            }

            cut.Add(end);
        }

        return cut;
    }

    /// <summary>The part of a stack of columns that falls inside the plot.</summary>
    /// <param name="columns">Each column's position and the two values that bound the band.</param>
    /// <param name="plot">The plot to cut them to.</param>
    /// <returns>The visible columns, with the ends of a cut segment interpolated.</returns>
    internal static List<(double X, double Below, double Top)> Stack(
        IReadOnlyList<(double X, double Below, double Top)> columns,
        ChartPlot plot) {

        List<(double X, double Below, double Top)> cut = [];

        if (columns.Count == 1) {
            if (columns[0].X >= plot.Left && columns[0].X <= plot.Right) {
                cut.Add(columns[0]);
            }

            return cut;
        }

        for (int index = 1; index < columns.Count; index++) {
            (double fromX, double fromBelow, double fromTop) = columns[index - 1];
            (double toX, double toBelow, double toTop) = columns[index];
            if (Span(fromX, toX, plot) is not (double from, double to)) {
                continue;
            }

            (double X, double Below, double Top) start = (
                Place(fromX, toX, from, plot),
                fromBelow + (from * (toBelow - fromBelow)),
                fromTop + (from * (toTop - fromTop)));
            (double X, double Below, double Top) end = (
                Place(fromX, toX, to, plot),
                fromBelow + (to * (toBelow - fromBelow)),
                fromTop + (to * (toTop - fromTop)));

            if (cut.Count == 0 || cut[^1] != start) {
                cut.Add(start);
            }

            cut.Add(end);
        }

        return cut;
    }

    private static double Place(double from, double to, double fraction, ChartPlot plot) =>
        Math.Clamp(from + (fraction * (to - from)), plot.Left, plot.Right);

    // Where a segment enters and leaves the plot, as fractions of its length, or null when it misses.
    private static (double From, double To)? Span(double from, double to, ChartPlot plot) {
        double width = to - from;

        if (width == 0) {
            return from >= plot.Left && from <= plot.Right ? (0, 1) : null;
        }

        double enter = Math.Max(0, Math.Min((plot.Left - from) / width, (plot.Right - from) / width));
        double leave = Math.Min(1, Math.Max((plot.Left - from) / width, (plot.Right - from) / width));

        return enter <= leave ? (enter, leave) : null;
    }
}
