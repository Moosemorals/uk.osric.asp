// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>The template language: text, plus <c>&lt;% %&gt;</c> blocks of C# and <c>&lt;%! %&gt;</c> declarations.</summary>
internal static class TemplateTokenizer {

    /// <summary>Splits a page template into its text, code and declaration tokens.</summary>
    /// <param name="input">The template as its author wrote it.</param>
    /// <returns>The tokens, and one error naming the opener of an unterminated block.</returns>
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
            // A C# statement cannot begin with '!', so an adjacent one is unambiguous: it marks the
            // block as namespace-level declarations rather than statements.
            bool isDeclaration = codeStart + 2 < input.Length && input[codeStart + 2] == '!';
            int contentStart = isDeclaration ? codeStart + 3 : codeStart + 2;

            int codeEnd = input.IndexOf("%>", contentStart, StringComparison.Ordinal);
            if (codeEnd == -1) {
                return new TokenizeResult(tokens, [isDeclaration
                    ? UnterminatedBlock(input, codeStart, "declaration", "<%!")
                    : UnterminatedBlock(input, codeStart, "code", "<%")]);
            }
            tokens.Add(new Token(isDeclaration ? TokenType.Declaration : TokenType.Code,
                input[contentStart..codeEnd],
                TemplateLines.PositionOf(input, contentStart)));
            index = codeEnd + 2;
        }

        return new TokenizeResult(tokens, []);
    }

    // The author is given a position in the text they typed, not in the generated source, and it
    // names the opener they have to close rather than the end of the template.
    private static PageDiagnostic UnterminatedBlock(string input, int codeStart, string kind, string opener) {
        return new PageDiagnostic(
            PageDiagnosticSeverity.Error,
            TemplateLines.PositionOf(input, codeStart),
            $"TEMPLATE001: unterminated {kind} block; the '{opener}' has no matching '%>'.");
    }
}
