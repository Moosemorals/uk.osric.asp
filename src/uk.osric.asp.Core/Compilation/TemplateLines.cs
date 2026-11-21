// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>Template line-and-column arithmetic shared by the tokenizer, the generator and the editor gutter.</summary>
/// <remarks>
/// \r\n, \n and a lone \r are line breaks; other Unicode separators are not. Keeping the rule here
/// means the tokenizer's positions, the generated #line mapping and the editor's line numbers cannot
/// disagree.
/// </remarks>
public static class TemplateLines {

    /// <summary>The lines of a template, as the editor and <c>read_page</c> count them.</summary>
    /// <param name="input">The template source.</param>
    /// <returns>
    /// Every physical line without its break. A trailing break ends a line rather than starting an
    /// empty one, so <c>"one\n"</c> is one line and an empty template is one empty line — the count
    /// <see cref="Count"/> reports.
    /// </returns>
    /// <remarks>
    /// <c>ShellText.Split</c> answers the POSIX count instead and must not be unified with this: a page
    /// that does not end with a newline would make an edit address the wrong line.
    /// </remarks>
    public static string[] Split(string input) {
        ArgumentNullException.ThrowIfNull(input);

        List<string> lines = [];
        using StringReader reader = new(input);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            lines.Add(line);
        }

        if (input.Length == 0 || input.EndsWith('\n') || input.EndsWith('\r')) {
            lines.Add("");
        }

        return [.. lines];
    }

    /// <summary>The number of lines in a template.</summary>
    /// <param name="input">The template source.</param>
    /// <returns>The line count, which is at least one: an empty template is one empty line.</returns>
    public static int Count(string input) {
        ArgumentNullException.ThrowIfNull(input);

        int count = 1;

        for (int index = 0; index < input.Length; index++) {
            if (input[index] == '\r' && index + 1 < input.Length && input[index + 1] == '\n') {
                index++;
            }

            if (input[index] is '\r' or '\n') {
                count++;
            }
        }

        return count;
    }

    /// <summary>The template position of a 0-based offset.</summary>
    internal static TemplatePosition PositionOf(string input, int offset) {
        ArgumentNullException.ThrowIfNull(input);

        return Map(input).PositionOf(offset);
    }

    /// <summary>Reads a template's line starts once, so every position in it is a binary search.</summary>
    /// <param name="input">The template source.</param>
    /// <returns>The map a tokenizer asks for every position it records.</returns>
    /// <remarks>
    /// A token's position used to be found by walking the template from the start, and a tokenizer
    /// asks for two positions per block: on a large page that is quadratic in the length of the
    /// template, which made generating and shape-checking the 45 KB editor page cost a quarter of a
    /// second. One pass over the template answers every position after it.
    /// </remarks>
    internal static TemplateLineMap Map(string input) {
        ArgumentNullException.ThrowIfNull(input);

        List<int> starts = [0];
        for (int index = 0; index < input.Length; index++) {
            if (input[index] == '\r' && index + 1 < input.Length && input[index + 1] == '\n') {
                index++;
            }

            if (input[index] is '\r' or '\n') {
                starts.Add(index + 1);
            }
        }

        return new TemplateLineMap(input, [.. starts]);
    }

    /// <summary>Advances a template position over a piece of template text.</summary>
    /// <param name="position">The position before the text.</param>
    /// <param name="text">The text to move past.</param>
    /// <returns>The position immediately after the text.</returns>
    internal static TemplatePosition Advance(this TemplatePosition position, string text) {
        int line = position.Line;
        int column = position.Column;

        for (int index = 0; index < text.Length; index++) {
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') {
                index++;
                line++;
                column = 1;
            } else if (text[index] is '\r' or '\n') {
                line++;
                column = 1;
            } else {
                column++;
            }
        }

        return new TemplatePosition(line, column);
    }
}

/// <summary>A template's line starts, so the position of any offset is a search rather than a scan.</summary>
internal sealed class TemplateLineMap {

    private readonly string _input;
    private readonly int[] _starts;

    internal TemplateLineMap(string input, int[] starts) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(starts);

        _input = input;
        _starts = starts;
    }

    /// <summary>The template position of a 0-based offset.</summary>
    /// <param name="offset">The offset to place, from zero to the template's length.</param>
    /// <returns>The one-based line and column of the character at that offset.</returns>
    public TemplatePosition PositionOf(int offset) {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _input.Length);

        // An offset that splits a CRLF pair is still on the line the pair opened: the walk this
        // replaced counted the lone '\r' as a break, so the '\n' it belongs to is the line's first
        // character. Nothing the tokenizer asks for lands there, but a caller with a raw offset might.
        if (offset > 0 && offset < _input.Length && _input[offset - 1] == '\r' && _input[offset] == '\n') {
            return PositionOf(offset + 1);
        }

        int low = 0;
        int high = _starts.Length - 1;
        while (low < high) {
            int middle = (low + high + 1) / 2;
            if (_starts[middle] <= offset) {
                low = middle;
            } else {
                high = middle - 1;
            }
        }

        return new TemplatePosition(low + 1, offset - _starts[low] + 1);
    }
}
