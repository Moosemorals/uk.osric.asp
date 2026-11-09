// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Globalization;

/// <summary>Formatting, escaping and palette values for the markup the charts generate.</summary>
/// <remarks>
/// <para>
/// Everything a drawing needs in order to be legible is written as a presentation attribute — the
/// stroke, the dash, the fill, the type size — so the same SVG reads correctly with no stylesheet at
/// all. That is what lets a chart be served on its own. The class names remain as hooks for the two
/// things that are page-only: the hover dim and the pinned state.
/// </para>
/// <para>
/// The palette is Okabe–Ito's colourblind-safe set with the yellow left out, because yellow on paper
/// is a highlight rather than a line. Four dash patterns cycle with the seven colours, so two series
/// can share a colour and still be told apart without relying on the colour at all.
/// </para>
/// </remarks>
public static class SvgText {

    /// <summary>How many series colours the palette defines.</summary>
    internal const int SeriesColours = 7;

    /// <summary>How many shades a heatmap uses.</summary>
    internal const int HeatLevels = 5;

    /// <summary>How much larger every chart's type is than the first draft's eleven-pixel labels.</summary>
    /// <remarks>
    /// A drawing's spacing is arithmetic rather than measured, so the distances that exist to hold a
    /// label scale with its type: the margins, the row heights and the end-of-line labels' offsets. The
    /// type grows into its room instead of into its neighbours.
    /// </remarks>
    internal const double TypeScale = 1.1;

    /// <summary>Roughly how wide one character is at the labels' size, for laying out a legend.</summary>
    internal const double CharacterWidth = 5.6 * TypeScale;

    private static readonly string[] Strokes = [
        "#0072B2", // blue
        "#D55E00", // vermillion
        "#009E73", // bluish green
        "#CC79A7", // reddish purple
        "#E69F00", // orange
        "#56B4E9", // sky blue
        "#000000", // black
    ];

    private static readonly string?[] Dashes = [null, "6 4", "2 3", "9 3 2 3"];

    private static readonly string[] HeatShades = ["#dbeafe", "#93c5fd", "#3b82f6", "#1d4ed8", "#1e3a8a"];

    /// <summary>Encodes text for an element body or an attribute value.</summary>
    /// <param name="value">The text, which may be null.</param>
    internal static string Escape(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Formats a coordinate or a value for an attribute, in the invariant culture.</summary>
    /// <param name="value">The number to write.</param>
    internal static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The CSS class for the nth series, cycling through the colours the palette defines.</summary>
    /// <param name="index">The series position, counted from zero.</param>
    public static string SeriesColour(int index) => "series-" + ((index % SeriesColours) + 1);

    /// <summary>The stroke for the nth series.</summary>
    /// <param name="index">The series position, counted from zero.</param>
    internal static string SeriesStroke(int index) => Strokes[Modulo(index, SeriesColours)];

    /// <summary>The dash pattern for the nth series, or null for a solid line.</summary>
    /// <param name="index">The series position, counted from zero.</param>
    internal static string? SeriesDash(int index) => Dashes[Modulo(index, Dashes.Length)];

    /// <summary>The fill for a heatmap level, from 1 (palest) to 5 (darkest).</summary>
    /// <param name="level">The level, clamped to the palette's range.</param>
    internal static string HeatFill(int level) => HeatShades[Math.Clamp(level, 1, HeatLevels) - 1];

    /// <summary>Estimates the width of a label, for wrapping a legend without measuring text.</summary>
    /// <param name="text">The label.</param>
    internal static double EstimateWidth(string text) => text.Length * CharacterWidth;

    private static int Modulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
