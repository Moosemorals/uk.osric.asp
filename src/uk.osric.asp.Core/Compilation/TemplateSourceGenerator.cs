// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

using System.Text;

/// <summary>Turns a page template into the generated C# source of its handler.</summary>
public static class TemplateSourceGenerator {

    private const string TemplateName = "page";

    // The page contract's namespace, as text: Core references nothing, so it cannot name the SDK at
    // compile time. PageApiPolicy holds the other copy of this value; PageScopeTests fails if the two
    // drift apart.
    private const string SdkNamespace = "uk.osric.asp.Sdk";

    /// <summary>Generates the handler source for a page template.</summary>
    /// <param name="template">The page template.</param>
    /// <returns>
    /// The generated source, or the errors and no source when the template does not parse. No partial
    /// source is generated for a template that does not parse: Roslyn would then report a syntax error
    /// where the text was cut off on top of the real one, and the author would fix the wrong thing.
    /// </returns>
    public static TemplateGenerationResult Generate(string template) {
        ArgumentNullException.ThrowIfNull(template);

        TokenizeResult tokenized = TemplateTokenizer.Tokenize(template);
        if (!tokenized.Success) {
            return new TemplateGenerationResult(null, tokenized.Errors);
        }

        foreach (Token token in tokenized.Tokens) {
            if (token.Type is TokenType.Write or TokenType.WriteRaw
                && NormalizeWriteExpression(token).Length == 0) {
                TemplatePosition opener = token.OpenerPosition
                    ?? throw new InvalidOperationException("A write token has no opener position.");
                return new TemplateGenerationResult(null, [
                    new PageDiagnostic(
                        PageDiagnosticSeverity.Error,
                        opener,
                        "TEMPLATE002: write expression is empty."),
                ]);
            }
        }

        StringBuilder result = new();

        // The generated using stays for the page's own convenience; the generated signature names the
        // SDK explicitly below, so a page type called IContext or IHandler cannot shadow it.
        result.Append($"using {SdkNamespace};");
        result.Append(Environment.NewLine);

        // Declarations are compile-time only, so hoisting them changes nothing about the body's
        // output order. Template order is also the using order: a using directive cannot follow a
        // type declaration, and Roslyn reports that at the using's own template position.
        foreach (Token token in tokenized.Tokens) {
            if (token.Type == TokenType.Declaration) {
                AppendMapped(result, token);
            }
        }

        result.Append($$"""
            public class GeneratedPage : {{SdkNamespace}}.IHandler {
            public void Handle({{SdkNamespace}}.IContext ctx) {
        """);
        result.Append(Environment.NewLine);

        foreach (Token token in tokenized.Tokens) {
            switch (token.Type) {
                case TokenType.Text:
                    AppendText(result, token);
                    break;
                case TokenType.Code:
                    AppendMapped(result, token);
                    break;
                case TokenType.Write:
                case TokenType.WriteRaw:
                    AppendWrite(result, token);
                    break;
                case TokenType.Declaration:
                    // Emitted above the handler; a declaration block writes no output.
                    break;
            }
        }

        // The closing braces are generated, but an unclosed page block is reported here. Mapping the
        // postlude to one line past the template turns that author typo into a positioned diagnostic
        // instead of an unmapped error the runtime would treat as a compiler fault.
        result.Append($"#line {TemplateLines.Count(template) + 1} \"{TemplateName}\"");
        result.Append(Environment.NewLine);
        result.Append("}}");

        return new TemplateGenerationResult(new GeneratedSource(result.ToString()), []);
    }

    private static void AppendText(StringBuilder result, Token token) {
        result.Append("ctx.WriteRaw(");
        result.Append(ToCSharpLiteral(token.Value));
        result.Append(");");
        result.Append(Environment.NewLine);
    }

    private static void AppendMapped(StringBuilder result, Token token) {
        // Token.Position is template space, which is exactly what #line expects. Padding the first
        // line to the token's template column makes Roslyn's column match the author's column too.
        result.Append($"#line {token.Position.Line} \"{TemplateName}\"");
        result.Append(Environment.NewLine);
        result.Append(' ', token.Position.Column - 1);
        result.Append(token.Value);
        result.Append(Environment.NewLine);
        result.Append("#line default");
        result.Append(Environment.NewLine);
    }

    private static void AppendWrite(StringBuilder result, Token token) {
        TemplatePosition opener = token.OpenerPosition
            ?? throw new InvalidOperationException("A write token has no opener position.");
        string expression = NormalizeWriteExpression(token);
        int leadingWhitespaceLength = token.Value.Length - token.Value.TrimStart().Length;
        TemplatePosition expressionPosition = TemplateLines.Advance(
            token.Position,
            token.Value[..leadingWhitespaceLength]);

        result.Append($"#line {opener.Line} \"{TemplateName}\"");
        result.Append(Environment.NewLine);
        result.Append(' ', opener.Column - 1);
        result.Append(token.Type == TokenType.Write ? "ctx.Write(" : "ctx.WriteRaw(");
        result.Append(Environment.NewLine);

        result.Append($"#line {expressionPosition.Line} \"{TemplateName}\"");
        result.Append(Environment.NewLine);
        result.Append(' ', expressionPosition.Column - 1);
        result.Append(expression);
        result.Append(Environment.NewLine);
        result.Append("#line default");
        result.Append(Environment.NewLine);
        result.Append(");");
        result.Append(Environment.NewLine);
    }

    private static string NormalizeWriteExpression(Token token) {
        string expression = token.Value.Trim();
        return expression.EndsWith(';')
            ? expression[..^1]
            : expression;
    }

    // A verbatim literal keeps newlines, quotes and backslashes legible in the generated source, and
    // doubling a quote is the only escape it needs. That keeps Core free of Roslyn.
    private static string ToCSharpLiteral(string value) {
        return $"@\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
