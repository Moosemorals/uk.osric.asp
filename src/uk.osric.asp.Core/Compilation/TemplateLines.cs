// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>Template line-and-column arithmetic shared by the tokenizer and the generator.</summary>
/// <remarks>
/// \r\n, \n and a lone \r are line breaks; other Unicode separators are not. Keeping the rule here
/// means the tokenizer's positions and the generated #line mapping cannot disagree.
/// </remarks>
internal static class TemplateLines {

    /// <summary>The number of lines in a template.</summary>
    public static int Count(string input) {
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
    public static TemplatePosition PositionOf(string input, int offset) {
        int line = 1;
        int lineStart = 0;

        for (int index = 0; index < offset; index++) {
            if (input[index] == '\r' && index + 1 < offset && input[index + 1] == '\n') {
                index++;
            }

            if (input[index] is '\r' or '\n') {
                line++;
                lineStart = index + 1;
            }
        }

        return new TemplatePosition(line, offset - lineStart + 1);
    }

    /// <summary>Advances a template position over a piece of template text.</summary>
    /// <param name="position">The position before the text.</param>
    /// <param name="text">The text to move past.</param>
    /// <returns>The position immediately after the text.</returns>
    public static TemplatePosition Advance(TemplatePosition position, string text) {
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
