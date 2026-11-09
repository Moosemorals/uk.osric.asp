// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Text;

using System.Buffers;
using System.Globalization;
using System.Text;

/// <summary>Turns untrusted text into one bounded line fit to be quoted in a message.</summary>
/// <remarks>
/// <para>
/// A page path, tool name or argument name is data a model or an owner wrote, so quoting it back in
/// a message is quoting untrusted text: it may carry line breaks that fake a new message, control
/// characters, or bidi overrides that make a value read as something else. Every run of whitespace
/// collapses to a single space, everything that cannot be printed is dropped, and the result stops
/// at <see cref="MaximumLength"/> text elements, ending in <see cref="Ellipsis"/> when it was
/// shortened.
/// </para>
/// <para>
/// Truncation counts text elements rather than UTF-16 code units so a surrogate pair, a combined
/// accent or an emoji is never cut in half; the code-unit cap keeps one pathological cluster — a
/// base character followed by thousands of combining marks — from defeating the limit. Dropping an
/// unpaired surrogate keeps the result valid Unicode, which is what
/// <see cref="LowercaseText.Normalize"/> and <see cref="string.Normalize()"/> require.
/// </para>
/// </remarks>
public static class MessageText {

    /// <summary>The longest sanitised value, counting the ellipsis as one text element.</summary>
    internal const int MaximumLength = 32;

    /// <summary>Appended when a value is shortened to <see cref="MaximumLength"/>.</summary>
    internal const string Ellipsis = "\u2026";

    private const int MaximumCodeUnits = MaximumLength * 4;

    /// <summary>Sanitises <paramref name="value"/> into one line of at most
    /// <see cref="MaximumLength"/> text elements.</summary>
    /// <param name="value">The untrusted text to quote. It is neither validated nor preserved.</param>
    /// <returns>The printable text, collapsed to one line, with no leading or trailing space.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static string Sanitize(string value) {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder collapsed = new(Math.Min(value.Length, MaximumCodeUnits));
        bool pendingSpace = false;
        int index = 0;
        while (index < value.Length) {
            OperationStatus status = Rune.DecodeFromUtf16(value.AsSpan(index), out Rune rune, out int consumed);
            index += Math.Max(consumed, 1);
            if (status != OperationStatus.Done) {
                continue;
            }

            if (Rune.IsWhiteSpace(rune)) {
                pendingSpace = collapsed.Length > 0;
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format) {
                continue;
            }

            if (pendingSpace) {
                collapsed.Append(' ');
                pendingSpace = false;
            }

            collapsed.Append(value, index - consumed, consumed);
        }

        return Clamp(collapsed.ToString());
    }

    private static string Clamp(string text) {
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        if (starts.Length <= MaximumLength && text.Length <= MaximumCodeUnits) {
            return text;
        }

        StringBuilder shortened = new();
        for (int position = 0; position < starts.Length; position++) {
            int end = position + 1 < starts.Length ? starts[position + 1] : text.Length;
            int length = end - starts[position];
            if (position == MaximumLength - 1 || shortened.Length + length > MaximumCodeUnits) {
                break;
            }

            shortened.Append(text, starts[position], length);
        }

        return shortened.Append('…').ToString();
    }
}
