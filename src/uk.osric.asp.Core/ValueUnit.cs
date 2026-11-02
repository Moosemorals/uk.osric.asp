// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>Chooses the unit a value axis is drawn in.</summary>
/// <remarks>
/// The instruments record seconds, and stored data stays seconds. A chart whose whole range is a
/// fraction of a second is unreadable in seconds — <c>0.00025</c> is four zeros before anything
/// happens — so a duration axis below one second is drawn in milliseconds instead.
/// </remarks>
public static class ValueUnit {

    /// <summary>Rescales a duration range into seconds or milliseconds.</summary>
    /// <param name="min">The lowest value in seconds.</param>
    /// <param name="max">The highest value in seconds.</param>
    /// <returns>
    /// Milliseconds when the top of the range is below a second, otherwise the range unchanged.
    /// </returns>
    public static ChartScale ForSeconds(double min, double max) {
        if (double.IsFinite(max) && max < 1) {
            return new ChartScale(min * 1000, max * 1000, "milliseconds");
        }

        return new ChartScale(min, max, "seconds");
    }
}
