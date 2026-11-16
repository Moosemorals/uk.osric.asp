// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

using System.Text;

/// <summary>The literal text of a template: its source with every comment construct removed.</summary>
/// <remarks>
/// <para>
/// A page with no code is served from its stored source rather than compiled, and a comment is
/// neither code nor text, so the body a browser receives is the source less its comments. That is
/// the one place a template's constructs are taken out of the bytes rather than blanked:
/// <see cref="TemplateScriptSkeleton"/> keeps a construct's width so a parser's positions hold, while
/// this returns what the visitor should have got.
/// </para>
/// <para>
/// A comment removes itself entirely, as a code block that writes nothing does, rather than leaving
/// whitespace in its place.
/// </para>
/// </remarks>
public static class TemplateComments {

    /// <summary>Removes every comment construct from a template.</summary>
    /// <param name="template">The template as its author wrote it.</param>
    /// <returns>
    /// The template's text with each <c>&lt;%# … %&gt;</c> removed and everything else unchanged, or
    /// the template as it was when it holds no comment or cannot be tokenized — a template the
    /// tokenizer refused is stored with its error and never served, so dropping the rest of it would
    /// answer with less than the author wrote.
    /// </returns>
    public static string Remove(string template) {
        ArgumentNullException.ThrowIfNull(template);

        if (!template.Contains("<%", StringComparison.Ordinal)) {
            return template;
        }

        TokenizeResult tokenized = TemplateTokenizer.Tokenize(template);
        if (!tokenized.IsSuccess) {
            return template;
        }

        StringBuilder text = new(template.Length);
        bool commented = false;
        foreach (Token token in tokenized.Tokens) {
            if (token.Type is TokenType.Comment) {
                commented = true;
            } else if (token.Type is TokenType.Text) {
                text.Append(token.Value);
            }
        }

        return commented ? text.ToString() : template;
    }
}
