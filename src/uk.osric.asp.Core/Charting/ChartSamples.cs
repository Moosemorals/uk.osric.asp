// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>The range arithmetic the charts share.</summary>
internal static class ChartSamples {

    /// <summary>Widens a flat range so a line does not sit on the axis.</summary>
    /// <param name="min">The lowest value.</param>
    /// <param name="max">The highest value, equal to <paramref name="min"/>.</param>
    internal static (double Min, double Max) Widen(double min, double max) {
        double pad = min == 0 ? 1 : Math.Abs(min) * 0.1;

        return (min - pad, max + pad);
    }
}
