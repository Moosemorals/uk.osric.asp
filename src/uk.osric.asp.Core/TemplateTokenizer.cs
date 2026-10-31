// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;


public record Token(TokenType Type, string Value);

public enum TokenType {
    Text,
    Code,
}

public static class TemplateTokenizer {

    /// <summary>Splits a page template into its text and code tokens.</summary>
    /// <param name="input">The template as its author wrote it.</param>
    /// <returns>The tokens, and one error naming the opener of an unterminated code block.</returns>
    public static TokenizeResult Tokenize(string input) {
        ArgumentNullException.ThrowIfNull(input);

        List<Token> tokens = [];
        int index = 0;
        while (index < input.Length) {
            int codeStart = input.IndexOf("<%", index, StringComparison.Ordinal);
            if (codeStart == -1) {
                tokens.Add(new Token(TokenType.Text, input[index..]));
                break;
            }
            if (codeStart > index) {
                tokens.Add(new Token(TokenType.Text, input[index..codeStart]));
            }
            int codeEnd = input.IndexOf("%>", codeStart + 2, StringComparison.Ordinal);
            if (codeEnd == -1) {
                return new TokenizeResult(tokens, [UnterminatedCodeBlock(input, codeStart)]);
            }
            tokens.Add(new Token(TokenType.Code, input[(codeStart + 2)..codeEnd]));
            index = codeEnd + 2;
        }

        return new TokenizeResult(tokens, []);
    }

    // The author is given a position in the text they typed, not in the generated source, and it
    // names the opener they have to close rather than the end of the template.
    private static string UnterminatedCodeBlock(string input, int codeStart) {
        int line = 1;
        int lineStart = 0;
        for (int position = 0; position < codeStart; position++) {
            if (input[position] == '\n') {
                line++;
                lineStart = position + 1;
            }
        }

        int column = codeStart - lineStart + 1;
        return $"TEMPLATE001: unterminated code block; the '<%' at line {line}, column {column} has no matching '%>'.";
    }
}


