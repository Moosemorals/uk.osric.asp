// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Globalization;

/// <summary>Formats a budget figure to at most three digits and a unit letter, so a gauge stays
/// one short line however much a run has spent.</summary>
/// <remarks>
/// The conversation page draws seven figures in two columns and repeats each one over its meter.
/// A raw figure such as <c>16384</c> would not fit, so anything from a thousand is scaled and
/// shortened: <c>1.2k</c>, <c>16k</c>, <c>500k</c>, <c>1M</c>. The brief scale below ten keeps one
/// decimal and everything above it rounds to a whole unit, which is what caps the digits at three.
/// </remarks>
public static class BudgetAmount {

    private static readonly string[] Units = ["k", "M", "G", "T", "P", "E"];

    /// <summary>Formats <paramref name="value"/> as a compact figure such as <c>16k</c>.</summary>
    /// <param name="value">The figure to format, which may not be negative.</param>
    /// <returns>The figure with a unit letter only when it is at least a thousand.</returns>
    public static string Format(long value) {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (value < 1_000) {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        decimal scaled = value;
        int unit = -1;
        while (scaled >= 1_000 && unit < Units.Length - 1) {
            scaled /= 1_000;
            unit++;
        }

        scaled = scaled < 10
            ? Math.Round(scaled, 1, MidpointRounding.AwayFromZero)
            : Math.Round(scaled, 0, MidpointRounding.AwayFromZero);

        // Rounding 999.6k up would print a fourth digit, so it rolls over to the next unit instead.
        if (scaled >= 1_000 && unit < Units.Length - 1) {
            scaled /= 1_000;
            unit++;
        }

        return scaled.ToString("0.#", CultureInfo.InvariantCulture) + Units[unit];
    }
}
