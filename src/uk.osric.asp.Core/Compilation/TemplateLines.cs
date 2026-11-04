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
}
