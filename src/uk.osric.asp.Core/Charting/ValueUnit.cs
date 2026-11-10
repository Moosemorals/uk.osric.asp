// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>Chooses the unit a byte axis is drawn in.</summary>
/// <remarks>
/// Stored sizes stay bytes; a scale whose top is megabytes reads as <c>180</c> rather than
/// <c>180000000</c>, so the axis labels stay short.
/// </remarks>
public static class ValueUnit {

    /// <summary>Rescales a byte range into bytes, kilobytes or megabytes.</summary>
    /// <param name="min">The lowest value in bytes.</param>
    /// <param name="max">The highest value in bytes.</param>
    /// <returns>The range in the largest unit that keeps the labels short.</returns>
    public static ChartScale ForBytes(double min, double max) {
        if (double.IsFinite(max) && max >= 1_000_000) {
            return new ChartScale(min / 1_000_000, max / 1_000_000, "megabytes");
        }

        if (double.IsFinite(max) && max >= 1_000) {
            return new ChartScale(min / 1_000, max / 1_000, "kilobytes");
        }

        return new ChartScale(min, max, "bytes");
    }
}
