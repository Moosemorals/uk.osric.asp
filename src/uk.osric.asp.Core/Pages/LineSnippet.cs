// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

using uk.osric.asp.Core.Text;

/// <summary>A window of one source line, cut around a match and never shorter than the match.</summary>
/// <param name="Text">The snippet itself.</param>
/// <param name="Start">The 0-based offset in the line where <paramref name="Text"/> starts.</param>
/// <param name="Clipped">Whether the snippet is a fragment of the line rather than the whole of it.</param>
/// <remarks>
/// A search reads whole lines and answers a window of each one, so an answer stays small without
/// hiding the match. The match's own position is the caller's: it knows the index it asked about, and
/// finds it in the snippet at <c>index - Start</c>.
/// </remarks>
public readonly record struct LineSnippet(string Text, int Start, bool Clipped) {

    /// <summary>Cuts the window a match asks for.</summary>
    /// <param name="line">The whole line, without its break.</param>
    /// <param name="index">The 0-based offset of the match's first character.</param>
    /// <param name="length">The match's length.</param>
    /// <param name="context">The width the caller asked for, which the match may overrule.</param>
    /// <returns>
    /// The window, grown around the match: it is <c>context</c> bytes wide, or the match when that
    /// is longer, or the line when that is shorter, and it never leaves the line.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="line"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The match is not inside the line, or <paramref name="context"/> is not positive.
    /// </exception>
    public static LineSnippet Of(string line, int index, int length, int context) {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(context);

        if (index + length > line.Length) {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, $"A match at {index} of that length does not fit a line of {line.Length} code units.");
        }

        // The window contains the match, so the match's own bytes are the floor and the caller's
        // context is the budget. The line itself is the ceiling, because a short line has nothing more
        // to show. Right grows before left, which puts the odd byte on the side the match came from, as
        // a character-wide window did before the unit changed.
        int budget = Math.Max(context, Utf8.SizeInBytes(line.Substring(index, length)));
        int start = index;
        int end = index + length;
        int used = Utf8.SizeInBytes(line.Substring(index, length));

        while (true) {
            bool grew = false;
            if (end < line.Length) {
                int units = Utf8.NextCodePointUnits(line, end);
                int bytes = Utf8.SizeInBytes(line.Substring(end, units));
                if (used + bytes <= budget) {
                    end += units;
                    used += bytes;
                    grew = true;
                }
            }

            if (start > 0) {
                int units = Utf8.PreviousCodePointUnits(line, start);
                int bytes = Utf8.SizeInBytes(line.Substring(start - units, units));
                if (used + bytes <= budget) {
                    start -= units;
                    used += bytes;
                    grew = true;
                }
            }

            if (!grew) {
                break;
            }
        }

        return new LineSnippet(line[start..end], start, Clipped: start > 0 || end < line.Length);
    }
}
