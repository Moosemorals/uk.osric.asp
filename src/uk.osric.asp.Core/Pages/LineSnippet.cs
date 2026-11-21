// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

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
    /// The window, centred on the match: it is <c>context</c> characters wide, or the match when that
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
                nameof(length), length, $"A match at {index} of that length does not fit a line of {line.Length} characters.");
        }

        // The window contains the match, so the match's own length is the floor; the line itself is the
        // ceiling, because a short line has nothing more to show. The match sits in the middle, and a
        // window that would start before or after the line is pushed inside it.
        int width = Math.Min(Math.Max(context, length), line.Length);
        int start = Math.Clamp(index - ((width - length) / 2), 0, line.Length - width);

        return new LineSnippet(
            line.Substring(start, width),
            start,
            Clipped: start > 0 || start + width < line.Length);
    }
}
