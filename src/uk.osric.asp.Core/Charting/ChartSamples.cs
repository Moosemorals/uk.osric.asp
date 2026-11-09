// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>The spans the charts measure from a set of samples.</summary>
internal static class ChartSamples {

    /// <summary>The earliest and latest instant in a set of samples.</summary>
    /// <param name="points">The samples to measure.</param>
    /// <returns>The bounds, or the Unix epoch twice when there are no samples.</returns>
    internal static (DateTimeOffset Earliest, DateTimeOffset Latest) Instants(IEnumerable<ChartPoint> points) {
        ArgumentNullException.ThrowIfNull(points);

        bool any = false;
        DateTimeOffset earliest = default;
        DateTimeOffset latest = default;

        foreach (ChartPoint point in points) {
            if (!any) {
                earliest = latest = point.At;
                any = true;
                continue;
            }

            earliest = point.At < earliest ? point.At : earliest;
            latest = point.At > latest ? point.At : latest;
        }

        return any
            ? (earliest, latest)
            : (DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }

    /// <summary>Widens a flat range so a line does not sit on the axis.</summary>
    /// <param name="min">The lowest value.</param>
    /// <param name="max">The highest value, equal to <paramref name="min"/>.</param>
    internal static (double Min, double Max) Widen(double min, double max) {
        double pad = min == 0 ? 1 : Math.Abs(min) * 0.1;

        return (min - pad, max + pad);
    }
}
