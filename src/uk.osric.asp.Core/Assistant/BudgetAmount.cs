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

        decimal magnitude = value;
        int unit = -1;
        while (magnitude >= 1_000 && unit < Units.Length - 1) {
            magnitude /= 1_000;
            unit++;
        }

        // Keep integer midpoints exact; converting 9,950 to double makes 9.95 fall just below its tie.
        magnitude = magnitude < 10
            ? decimal.Round(magnitude, 1, MidpointRounding.AwayFromZero)
            : decimal.Round(magnitude, 0, MidpointRounding.AwayFromZero);

        if (magnitude >= 1_000 && unit < Units.Length - 1) {
            magnitude /= 1_000;
            unit++;
        }

        return magnitude.ToString("0.#", CultureInfo.InvariantCulture) + Units[unit];
    }

    /// <summary>Formats a measured value as at most three significant figures and a unit letter.</summary>
    /// <param name="value">The figure to format; it may be negative.</param>
    /// <returns>
    /// The figure with a unit letter from a thousand upwards, and up to three significant figures
    /// below it, so an axis tick reads <c>1.2k</c> or <c>0.25</c> rather than <c>1.048576e+06</c>.
    /// </returns>
    public static string Format(double value) {
        if (!double.IsFinite(value)) {
            return "0";
        }

        string sign = value < 0 ? "-" : string.Empty;
        double magnitude = Math.Abs(value);

        if (magnitude < 1_000) {
            return sign + Significant(magnitude);
        }

        int unit = -1;
        while (magnitude >= 1_000 && unit < Units.Length - 1) {
            magnitude /= 1_000;
            unit++;
        }

        magnitude = magnitude < 10
            ? Math.Round(magnitude, 1, MidpointRounding.AwayFromZero)
            : Math.Round(magnitude, 0, MidpointRounding.AwayFromZero);

        // Rounding 999.6k up would print a fourth digit, so it rolls over to the next unit instead.
        if (magnitude >= 1_000 && unit < Units.Length - 1) {
            magnitude /= 1_000;
            unit++;
        }

        return sign + magnitude.ToString("0.#", CultureInfo.InvariantCulture) + Units[unit];
    }

    // Three significant figures, without an exponent and without trailing zeros. A figure at or above
    // one keeps its integer zeros: 600 is 600, not 6.
    private static string Significant(double value) {
        if (value == 0) {
            return "0";
        }

        int decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(value)), 0, 9);
        string text = value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return text.Contains('.')
            ? text.TrimEnd('0').TrimEnd('.')
            : text;
    }
}
