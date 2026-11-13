// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>The result of replacing a bounded 1-based range of page lines.</summary>
/// <param name="Content">The new source, or null when validation failed.</param>
/// <param name="Error">A safe validation error, or null when replacement succeeded.</param>
public sealed record PageRangeEditResult(string? Content, string? Error) {
    /// <summary>Whether the replacement is valid.</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>One replacement of inclusive physical template-source lines.</summary>
/// <param name="StartLine">The first 1-based physical line to replace.</param>
/// <param name="EndLine">The last 1-based physical line to replace, inclusive.</param>
/// <param name="Replacement">The replacement source, which may contain line breaks.</param>
/// <param name="ExpectFirst">
/// The exact text the first line must still hold, or null to accept whatever is there. A mismatch
/// refuses the edit, so a line number that is off by one cannot silently replace the wrong line.
/// </param>
/// <param name="ExpectLast">The exact text the last line must still hold, or null to accept whatever is there.</param>
public sealed record PageLineEdit(
    int StartLine,
    int EndLine,
    string Replacement,
    string? ExpectFirst = null,
    string? ExpectLast = null);

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
        int maximumChars) =>
        Replace(source, startLine, endLine, replacement, maximumChars, expectFirst: null, expectLast: null);

    /// <summary>Replaces the selected lines when they still hold the text the caller expected.</summary>
    /// <param name="source">The complete current page source.</param>
    /// <param name="startLine">The first 1-based physical template-source line to replace.</param>
    /// <param name="endLine">The last 1-based physical template-source line to replace, inclusive.</param>
    /// <param name="replacement">The replacement source, which may contain line breaks.</param>
    /// <param name="maximumChars">The maximum resulting source length.</param>
    /// <param name="expectFirst">The exact text of <paramref name="startLine"/>, or null to accept whatever is there.</param>
    /// <param name="expectLast">The exact text of <paramref name="endLine"/>, or null to accept whatever is there.</param>
    public static PageRangeEditResult Replace(
        string source,
        int startLine,
        int endLine,
        string replacement,
        int maximumChars,
        string? expectFirst,
        string? expectLast) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChars);

        List<(int Start, int ContentEnd)> lines = GetLines(source);
        if (!TryResolve(lines, startLine, endLine, out int start, out int end)) {
            return Invalid("The requested line range is outside the page.");
        }
        if (!HasExpectedLines(lines, source, startLine, endLine, expectFirst, expectLast, out string? expectationError)) {
            return Invalid(expectationError!);
        }

        long resultingLength = (long)source.Length - (end - start) + replacement.Length;
        if (resultingLength > maximumChars) {
            return Invalid("The resulting page exceeds the configured size limit.");
        }

        return new PageRangeEditResult(source[..start] + replacement + source[end..], null);
    }

    /// <summary>Applies several line replacements as one edit, refusing overlapping ranges or oversized output.</summary>
    /// <param name="source">The complete current page source.</param>
    /// <param name="edits">The replacements, whose line numbers all refer to <paramref name="source"/>.</param>
    /// <param name="maximumChars">The maximum resulting source length.</param>
    /// <returns>
    /// The source with every edit applied, or a refusal. The edits are applied from the bottom up, so
    /// one replacement's line numbers do not shift another's.
    /// </returns>
    public static PageRangeEditResult ReplaceAll(
        string source,
        IReadOnlyList<PageLineEdit> edits,
        int maximumChars) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumChars);

        if (edits.Count == 0) {
            return Invalid("No edits were supplied.");
        }

        List<(int Start, int ContentEnd)> lines = GetLines(source);
        List<(int Start, int End, string Replacement)> resolved = [];
        List<(int StartLine, int EndLine)> ranges = [];
        long resultingLength = source.Length;
        foreach (PageLineEdit edit in edits) {
            ArgumentNullException.ThrowIfNull(edit);
            ArgumentNullException.ThrowIfNull(edit.Replacement);

            if (!TryResolve(lines, edit.StartLine, edit.EndLine, out int start, out int end)) {
                return Invalid($"Edit at line {edit.StartLine} is outside the page.");
            }
            if (!HasExpectedLines(lines, source, edit.StartLine, edit.EndLine, edit.ExpectFirst, edit.ExpectLast, out string? expectationError)) {
                return Invalid(expectationError!);
            }

            resolved.Add((start, end, edit.Replacement));
            ranges.Add((edit.StartLine, edit.EndLine));
            resultingLength += edit.Replacement.Length - (end - start);
        }

        ranges.Sort((left, right) => left.StartLine.CompareTo(right.StartLine));
        for (int index = 1; index < ranges.Count; index++) {
            if (ranges[index].StartLine <= ranges[index - 1].EndLine) {
                return Invalid($"Edits overlap at line {ranges[index].StartLine}; each edit must name a distinct range.");
            }
        }

        if (resultingLength > maximumChars) {
            return Invalid("The resulting page exceeds the configured size limit.");
        }

        resolved.Sort((left, right) => right.Start.CompareTo(left.Start));
        string result = source;
        foreach ((int start, int end, string replacement) in resolved) {
            result = result[..start] + replacement + result[end..];
        }

        return new PageRangeEditResult(result, null);
    }

    private static bool TryResolve(
        List<(int Start, int ContentEnd)> lines,
        int startLine,
        int endLine,
        out int start,
        out int end) {
        start = 0;
        end = 0;
        if (startLine < 1 || endLine < startLine || endLine > lines.Count) {
            return false;
        }

        start = lines[startLine - 1].Start;
        end = lines[endLine - 1].ContentEnd;
        return true;
    }

    private static bool HasExpectedLines(
        List<(int Start, int ContentEnd)> lines,
        string source,
        int startLine,
        int endLine,
        string? expectFirst,
        string? expectLast,
        out string? error) {
        error = null;
        if (expectFirst is not null) {
            string actual = source[lines[startLine - 1].Start..lines[startLine - 1].ContentEnd];
            if (!string.Equals(actual, expectFirst, StringComparison.Ordinal)) {
                error = $"Line {startLine} is not the expected text, so nothing was changed. Re-read the page and edit again.";
                return false;
            }
        }

        if (expectLast is not null) {
            string actual = source[lines[endLine - 1].Start..lines[endLine - 1].ContentEnd];
            if (!string.Equals(actual, expectLast, StringComparison.Ordinal)) {
                error = $"Line {endLine} is not the expected text, so nothing was changed. Re-read the page and edit again.";
                return false;
            }
        }

        return true;
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
