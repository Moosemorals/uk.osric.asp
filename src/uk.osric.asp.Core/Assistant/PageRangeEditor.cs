// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>The result of replacing a bounded 1-based range of page lines.</summary>
/// <param name="Content">The new source, or null when validation failed.</param>
/// <param name="Error">A safe validation error, or null when replacement succeeded.</param>
public sealed record PageRangeEditResult(string? Content, string? Error) {
    /// <summary>Whether the replacement is valid.</summary>
    public bool Success => Error is null;
}

/// <summary>Replaces inclusive physical lines of stored template source without changing surrounding bytes.</summary>
/// <remarks>
/// Lines are counted before template tokenization, C# generation, or HTML rendering. Literal HTML,
/// code, and template delimiters all occupy lines exactly where they appear in the stored source.
/// CRLF is one line break; bare CR and LF are each one. A final line break creates an empty final line.
/// </remarks>
public static class PageRangeEditor {

    /// <summary>Replaces the selected lines and refuses invalid ranges or oversized output.</summary>
    /// <param name="source">The complete current page source.</param>
    /// <param name="startLine">The first 1-based physical template-source line to replace.</param>
    /// <param name="endLine">The last 1-based physical template-source line to replace, inclusive.</param>
    /// <param name="replacement">The replacement source, which may contain line breaks.</param>
    /// <param name="maximumChars">The maximum resulting source length.</param>
    public static PageRangeEditResult Replace(
        string source,
        int startLine,
        int endLine,
        string replacement,
        int maximumChars) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChars);

        List<(int Start, int ContentEnd)> lines = GetLines(source);
        if (startLine < 1 || endLine < startLine || endLine > lines.Count) {
            return Invalid("The requested line range is outside the page.");
        }

        int start = lines[startLine - 1].Start;
        int end = lines[endLine - 1].ContentEnd;
        long resultingLength = (long)source.Length - (end - start) + replacement.Length;
        if (resultingLength > maximumChars) {
            return Invalid("The resulting page exceeds the configured size limit.");
        }

        return new PageRangeEditResult(source[..start] + replacement + source[end..], null);
    }

    private static List<(int Start, int ContentEnd)> GetLines(string source) {
        List<(int Start, int ContentEnd)> lines = [];
        int lineStart = 0;
        for (int index = 0; index < source.Length; index++) {
            if (source[index] is not ('\r' or '\n')) {
                continue;
            }

            lines.Add((lineStart, index));
            if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n') {
                index++;
            }
            lineStart = index + 1;
        }

        lines.Add((lineStart, source.Length));
        return lines;
    }

    private static PageRangeEditResult Invalid(string error) => new(null, error);
}
