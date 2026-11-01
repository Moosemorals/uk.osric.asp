// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>The template language: text, plus <c>&lt;% %&gt;</c> blocks of C#.</summary>
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
                tokens.Add(new Token(TokenType.Text, input[index..], TemplateLines.PositionOf(input, index)));
                break;
            }
            if (codeStart > index) {
                tokens.Add(new Token(TokenType.Text, input[index..codeStart], TemplateLines.PositionOf(input, index)));
            }
            int codeEnd = input.IndexOf("%>", codeStart + 2, StringComparison.Ordinal);
            if (codeEnd == -1) {
                return new TokenizeResult(tokens, [UnterminatedCodeBlock(input, codeStart)]);
            }
            tokens.Add(new Token(TokenType.Code, input[(codeStart + 2)..codeEnd],
                TemplateLines.PositionOf(input, codeStart + 2)));
            index = codeEnd + 2;
        }

        return new TokenizeResult(tokens, []);
    }

    // The author is given a position in the text they typed, not in the generated source, and it
    // names the opener they have to close rather than the end of the template.
    private static PageDiagnostic UnterminatedCodeBlock(string input, int codeStart) {
        return new PageDiagnostic(
            PageDiagnosticSeverity.Error,
            TemplateLines.PositionOf(input, codeStart),
            "TEMPLATE001: unterminated code block; the '<%' has no matching '%>'.");
    }
}


