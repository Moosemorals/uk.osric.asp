// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Text;

using System.Text;

/// <summary>The site's one size unit: the UTF-8 encoding of text.</summary>
/// <remarks>
/// <para>
/// A limit that bounds text bounds its UTF-8 encoding, and this type is the only place the unit is
/// spelled: <see cref="SizeInBytes"/> counts and <see cref="Clip"/> cuts, so a site and its tests
/// cannot disagree about whether "size" means bytes, code points or UTF-16 code units.
/// </para>
/// <para>
/// The encoding is <see cref="Encoding.UTF8"/>'s, replacement included: an unpaired surrogate is
/// three bytes, exactly as the encoder writes it, so the number a refusal quotes is the number the
/// store holds.
/// </para>
/// </remarks>
public static class Utf8 {

    /// <summary>How many bytes the text occupies once encoded as UTF-8.</summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The byte count of <paramref name="text"/>'s UTF-8 encoding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static int SizeInBytes(string text) {
        ArgumentNullException.ThrowIfNull(text);

        return Encoding.UTF8.GetByteCount(text);
    }

    /// <summary>The longest prefix of the text whose UTF-8 encoding is at most
    /// <paramref name="maxBytes"/>.</summary>
    /// <param name="text">The text to cut.</param>
    /// <param name="maxBytes">The byte budget. Zero answers the empty string.</param>
    /// <returns>The prefix, or <paramref name="text"/> itself when it already fits.</returns>
    /// <remarks>
    /// A cut never splits a code point, so the answer is valid text even when the budget falls inside
    /// one: the code point is dropped and the encoder counts the next one that fits.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxBytes"/> is negative.
    /// </exception>
    public static string Clip(string text, int maxBytes) {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        int used = 0;
        int index = 0;
        while (index < text.Length) {
            int units = NextCodePointUnits(text, index);
            int bytes = Encoding.UTF8.GetByteCount(text.AsSpan(index, units));
            if (used + bytes > maxBytes) {
                break;
            }

            used += bytes;
            index += units;
        }

        return index == text.Length ? text : text[..index];
    }

    /// <summary>How many UTF-16 code units the code point ending at an index occupies.</summary>
    /// <param name="text">The text to read.</param>
    /// <param name="index">The index one past the code point's last code unit.</param>
    /// <returns>
    /// Two for a surrogate pair and one otherwise; a lone surrogate is one unit, because the encoder
    /// replaces it with a single three-byte code point.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not between one and the text's length.
    /// </exception>
    public static int PreviousCodePointUnits(string text, int index) {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);

        return index >= 2 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2])
            ? 2
            : 1;
    }

    /// <summary>How many UTF-16 code units the code point at an index occupies.</summary>
    /// <param name="text">The text to read.</param>
    /// <param name="index">The index of the code point's first code unit.</param>
    /// <returns>
    /// Two for a surrogate pair and one otherwise; a lone surrogate is one unit, because the encoder
    /// replaces it with a single three-byte code point.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not inside <paramref name="text"/>.
    /// </exception>
    public static int NextCodePointUnits(string text, int index) {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, text.Length);

        return char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? 2
            : 1;
    }
}
