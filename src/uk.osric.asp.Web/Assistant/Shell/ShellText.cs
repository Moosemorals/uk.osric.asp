// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

using System.Text;

/// <summary>Lines and numbers, the way a shell counts them.</summary>
internal static class ShellText {

    /// <summary>The lines of a text, where a line is what a line break terminates.</summary>
    /// <param name="text">The text to split.</param>
    /// <remarks>
    /// A final line break ends the last line rather than starting an empty one, so <c>"one\n"</c> is
    /// one line and <c>""</c> is none — which is what POSIX counts, and what <c>wc -l</c> means.
    /// </remarks>
    public static string[] Split(string text) {
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines = [];
        int start = 0;
        for (int index = 0; index < text.Length; index++) {
            if (text[index] == '\n') {
                int end = index > start && text[index - 1] == '\r' ? index - 1 : index;
                lines.Add(text[start..end]);
                start = index + 1;
            }
        }
        if (start < text.Length) {
            lines.Add(text[start..]);
        }

        return [.. lines];
    }

    /// <summary>A text with each line's number and a tab in front of it.</summary>
    /// <param name="text">The text to number.</param>
    public static string Number(string text) {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder numbered = new(text.Length + 8);
        string[] lines = Split(text);
        for (int index = 0; index < lines.Length; index++) {
            if (index > 0) {
                _ = numbered.Append('\n');
            }
            _ = numbered.Append(index + 1).Append('\t').Append(lines[index]);
        }

        return numbered.ToString();
    }
}
