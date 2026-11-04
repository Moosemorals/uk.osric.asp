// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>
/// The template language: text, C# blocks, declaration blocks, and escaped or raw value writes.
/// </summary>
internal static class TemplateTokenizer {

    /// <summary>Splits a page template into text, code, declaration and write tokens.</summary>
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
            int openerLength = 2;
            TokenType type = TokenType.Code;
            if (codeStart + 2 < input.Length && input[codeStart + 2] == '=') {
                if (codeStart + 3 < input.Length && input[codeStart + 3] == '=') {
                    type = TokenType.WriteRaw;
                    openerLength = 4;
                } else {
                    type = TokenType.Write;
                    openerLength = 3;
                }
            } else if (codeStart + 2 < input.Length && input[codeStart + 2] == '!') {
                type = TokenType.Declaration;
                openerLength = 3;
            }

            int contentStart = codeStart + openerLength;

            int codeEnd = input.IndexOf("%>", contentStart, StringComparison.Ordinal);
            if (codeEnd == -1) {
                return new TokenizeResult(tokens, [UnterminatedBlock(input, codeStart, type)]);
            }
            TemplatePosition? openerPosition = type is TokenType.Write or TokenType.WriteRaw
                ? TemplateLines.PositionOf(input, codeStart)
                : null;
            tokens.Add(new Token(type,
                input[contentStart..codeEnd],
                TemplateLines.PositionOf(input, contentStart),
                openerPosition));
            index = codeEnd + 2;
        }

        return new TokenizeResult(tokens, []);
    }

    // The author is given a position in the text they typed, not in the generated source, and it
    // names the opener they have to close rather than the end of the template.
    private static PageDiagnostic UnterminatedBlock(string input, int codeStart, TokenType type) {
        (string kind, string opener) = type switch {
            TokenType.Declaration => ("declaration", "<%!"),
            TokenType.Write => ("write", "<%="),
            TokenType.WriteRaw => ("raw-write", "<%=="),
            _ => ("code", "<%"),
        };
        return new PageDiagnostic(
            PageDiagnosticSeverity.Error,
            TemplateLines.PositionOf(input, codeStart),
            $"TEMPLATE001: unterminated {kind} block; the '{opener}' has no matching '%>'.");
    }
}
