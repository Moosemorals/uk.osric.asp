// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;


public record Token(TokenType Type, string Value);

public enum TokenType {
    Text,
    Code,
}

public static class TemplateTokenizer {

    public static IEnumerable<Token> Tokenize(string input) {
        // Simple tokenizer implementation
        int index = 0;
        while (index < input.Length) {
            int codeStart = input.IndexOf("<%", index);
            if (codeStart == -1) {
                yield return new Token(TokenType.Text, input[index..]);
                break;
            }
            if (codeStart > index) {
                yield return new Token(TokenType.Text, input[index..codeStart]);
            }
            int codeEnd = input.IndexOf("%>", codeStart + 2);
            if (codeEnd == -1) {
                throw new ArgumentException("Unterminated code block.");
            }
            yield return new Token(TokenType.Code, input[(codeStart + 2)..codeEnd]);
            index = codeEnd + 2;
        }
    }
}


