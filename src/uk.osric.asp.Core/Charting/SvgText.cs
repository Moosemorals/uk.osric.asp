// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Globalization;
using System.Net;

/// <summary>Formatting and escaping for the markup the charts generate.</summary>
/// <remarks>
/// <see cref="WebUtility.HtmlEncode(string?)"/> already lives in the base class library; naming it
/// here keeps every escape in one place, next to the markup it guards. The number format and the
/// series colours are here for the same reason: both are part of what the generated markup means.
/// </remarks>
public static class SvgText {

    /// <summary>How many series colours the site's stylesheet defines.</summary>
    public const int SeriesColours = 5;

    /// <summary>Encodes text for an element body or an attribute value.</summary>
    /// <param name="value">The text, which may be null.</param>
    public static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Formats a coordinate or a value for an attribute, in the invariant culture.</summary>
    /// <param name="value">The number to write.</param>
    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The CSS class for the nth series, cycling through the colours the sheet defines.</summary>
    /// <param name="index">The series position, counted from zero.</param>
    public static string SeriesColour(int index) => "series-" + ((index % SeriesColours) + 1);
}
