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

    public static string GenerateTemplateSource(string template) {

        StringBuilder result = new();

        result.Append("""
            using uk.osric.asp.Core;
            public class GeneratedPage : IHandler {
            public void Handle(IContext ctx) {
        """);

        foreach (Token token in TemplateTokenizer.Tokenize(template)) {
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

        return result.ToString();
    }

    // A verbatim literal keeps newlines, quotes and backslashes legible in the generated source, and
    // doubling a quote is the only escape it needs. That keeps Core free of Roslyn.
    private static string ToCSharpLiteral(string value) {
        return $"@\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
