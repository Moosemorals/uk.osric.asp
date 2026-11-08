// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>Turns a page template into the literal JavaScript a parser can read, without rendering it.</summary>
/// <remarks>
/// A script page is still a template: the JavaScript is the text a browser would have received if no
/// template construct had produced any output. The skeleton keeps that text exactly where the author
/// typed it, so a parser's line and column are page positions. Code, declaration and write constructs
/// are replaced by spaces — and every line break is kept — rather than removed, so tokens either side
/// of a construct stay separated; a write becomes <c>0</c> and spaces so an expression position stays
/// valid.
/// </remarks>
public static class TemplateScriptSkeleton {

    /// <summary>Builds the skeleton of a template.</summary>
    /// <param name="template">The page template as its author wrote it.</param>
    /// <returns>
    /// The template's literal text with its constructs blanked, or the tokenizer's
    /// <c>TEMPLATE001</c> errors and an empty source when the template cannot be tokenized.
    /// </returns>
    public static ScriptSkeletonResult Create(string template) {
        ArgumentNullException.ThrowIfNull(template);

        TokenizeResult tokenized = TemplateTokenizer.Tokenize(template);
        if (!tokenized.IsSuccess) {
            return new ScriptSkeletonResult(string.Empty, tokenized.Errors);
        }

        char[] source = template.ToCharArray();
        int cursor = 0;

        foreach (Token token in tokenized.Tokens) {
            if (token.Type is TokenType.Text) {
                cursor += token.Value.Length;
                continue;
            }

            int length = OpenerLength(token.Type) + token.Value.Length + 2;
            Blank(source, cursor, length, token.Type is TokenType.Write or TokenType.WriteRaw);
            cursor += length;
        }

        return new ScriptSkeletonResult(new string(source), []);
    }

    private static int OpenerLength(TokenType type) => type switch {
        TokenType.Code => 2,
        TokenType.Declaration => 3,
        TokenType.Write => 3,
        TokenType.WriteRaw => 4,
        _ => 0,
    };

    private static void Blank(char[] source, int start, int length, bool isWrite) {
        for (int index = start; index < start + length; index++) {
            if (source[index] is not ('\r' or '\n')) {
                source[index] = ' ';
            }
        }

        if (isWrite) {
            source[start] = '0';
        }
    }
}
