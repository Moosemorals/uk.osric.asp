// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using System.Text;

/// <summary>Turns a page template into the generated C# source of its handler.</summary>
public static class TemplateSourceGenerator {

    private const string TemplateName = "page";

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
        result.Append(Environment.NewLine);

        foreach (Token token in tokenized.Tokens) {
            switch (token.Type) {
                case TokenType.Text:
                    result.Append("ctx.WriteRaw(");
                    result.Append(ToCSharpLiteral(token.Value));
                    result.Append(");");
                    result.Append(Environment.NewLine);
                    break;
                case TokenType.Code:
                    AppendCode(result, token);
                    break;
            }
        }

        // The closing braces are generated, but an unclosed page block is reported here. Mapping the
        // postlude to one line past the template turns that author typo into a positioned diagnostic
        // instead of an unmapped error the runtime would treat as a compiler fault.
        result.Append($"#line {TemplateLines.Count(template) + 1} \"{TemplateName}\"");
        result.Append(Environment.NewLine);
        result.Append("}}");

        return new TemplateParseResult(result.ToString(), []);
    }

    private static void AppendCode(StringBuilder result, Token token) {
        // Token.Position is template space, which is exactly what #line expects. Padding the first
        // line to the code's template column makes Roslyn's column match the author's column too.
        result.Append($"#line {token.Position.Line} \"{TemplateName}\"");
        result.Append(Environment.NewLine);
        result.Append(' ', token.Position.Column - 1);
        result.Append(token.Value);
        result.Append(Environment.NewLine);
        result.Append("#line default");
        result.Append(Environment.NewLine);
    }

    // A verbatim literal keeps newlines, quotes and backslashes legible in the generated source, and
    // doubling a quote is the only escape it needs. That keeps Core free of Roslyn.
    private static string ToCSharpLiteral(string value) {
        return $"@\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
