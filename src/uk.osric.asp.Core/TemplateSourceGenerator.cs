// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using System.Text;

public static class TemplateSourceGenerator {

    /// <summary>The generated line on which the page's own first line appears.</summary>
    /// <remarks>
    /// The generator writes a fixed three-line header, and the page's text and code keep their
    /// newlines, so a diagnostic on generated line <c>n</c> belongs to page line
    /// <c>n - PageCodeFirstLine + 1</c>. The runtime uses this to report page lines rather than
    /// generated ones.
    /// </remarks>
    public const int PageCodeFirstLine = 3;

    /// <summary>Generates the handler source for a page template.</summary>
    /// <param name="template">The page template.</param>
    /// <returns>
    /// The generated source, or the errors and no source when the template does not parse. No partial
    /// source is generated for a template that does not parse: Roslyn would then report a syntax error
    /// where the text was cut off on top of the real one, and the author would fix the wrong thing.
    /// </returns>
    public static TemplateParseResult GenerateTemplateSource(string template) {
        ArgumentNullException.ThrowIfNull(template);

        TokenizeResult tokenized = TemplateTokenizer.Tokenize(template);
        if (!tokenized.Success) {
            return new TemplateParseResult(null, tokenized.Errors);
        }

        StringBuilder result = new();

        result.Append("""
            using uk.osric.asp.Core;
            public class GeneratedPage : IHandler {
            public void Handle(IContext ctx) {
        """);

        foreach (Token token in tokenized.Tokens) {
            switch (token.Type) {
                case TokenType.Text: {
                        result.Append("ctx.WriteRaw(");
                        result.Append(ToCSharpLiteral(token.Value));
                        result.Append(");");

                        break;
                    }
                case TokenType.Code:
                    result.Append(token.Value);
                    result.Append(Environment.NewLine);
                    break;
            }
        }

        result.Append("}}");

        return new TemplateParseResult(result.ToString(), []);
    }

    // A verbatim literal keeps newlines, quotes and backslashes legible in the generated source, and
    // doubling a quote is the only escape it needs. That keeps Core free of Roslyn.
    private static string ToCSharpLiteral(string value) {
        return $"@\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
