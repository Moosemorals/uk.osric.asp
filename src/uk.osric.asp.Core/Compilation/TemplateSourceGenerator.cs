// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

using System.Text;
using uk.osric.asp.Core.Pages;

/// <summary>Turns a page template into the generated C# source of its handler.</summary>
public static class TemplateSourceGenerator {

    private const string TemplateName = "page";

    // The scaffold is compiled with nullable analysis on, so page code sees `ctx.User` as `PageUser?`
    // the way the product's own code does. Every line the generator wrote is wrapped in the disabled
    // context and every mapped region in the enabled one, so a diagnostic can only ever be about the
    // page: an unmapped warning is dropped by PageCompiler.MapDiagnostics, and a generator bug would
    // then be invisible. The directive is outside the region on purpose - inside it, `#line` would map
    // the directive's own line onto the author's, shifting every position after it.
    private const string NullableEnable = "#nullable enable";
    private const string NullableDisable = "#nullable disable";

    // The page contract's namespace, as text: Core references nothing, so it cannot name the SDK at
    // compile time. PageApiPolicy reads this one rather than keeping a second copy.
    internal const string SdkNamespace = "uk.osric.asp.Sdk";

    // Fully qualified on purpose: a prelude `using System.Threading.Tasks;` would put the whole
    // namespace, and every type in it, in scope for page code.
    private const string TaskTypeName = "global::System.Threading.Tasks.Task";

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
        if (!tokenized.IsSuccess) {
            // The tokenizer saw a '<%', so there is code here whatever it made of the rest. A parse
            // failure must not read as a template with nothing to compile.
            return new TemplateGenerationResult(null, tokenized.Errors) { Kind = PageKind.Page };
        }

        bool hasCode = tokenized.Tokens.Any(token => token.Type != TokenType.Text);

        // A template made of declarations and nothing else is a library: there is nothing in it to
        // render, so the pages that name it compile it and it is never served. One byte of markup,
        // one code block, and it is a page again.
        PageKind kind = PageKind.Text;
        if (hasCode) {
            kind = IsLibrary(tokenized.Tokens) ? PageKind.Library : PageKind.Page;
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
                ]) { Kind = PageKind.Page };
            }
        }

        StringBuilder result = new();

        // The scaffold is nullable-oblivious from its first line: the compilation enables nullable
        // analysis, and only a mapped region turns it back on.
        result.Append(NullableDisable);
        result.Append(Environment.NewLine);

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

        // Always async, so `await` is an ordinary expression in page code. A template with no await
        // would then warn CS1998 on the generated signature, which is unmapped and would be dropped
        // by the diagnostic mapping - an invisible fault. Suppress it instead.
        result.Append("#pragma warning disable CS1998");
        result.Append(Environment.NewLine);
        result.Append($$"""
            public class GeneratedPage : {{SdkNamespace}}.IHandler {
            public async {{TaskTypeName}} HandleAsync({{SdkNamespace}}.IContext ctx) {
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
        result.Append(Environment.NewLine);
        result.Append("#pragma warning restore CS1998");

        return new TemplateGenerationResult(new GeneratedSource(result.ToString()), []) { Kind = kind };
    }

    // Declarations are compile-time only, so a template with nothing but declaration blocks has
    // nothing to render. Whitespace between the blocks is layout for the author's eye, not markup.
    private static bool IsLibrary(IReadOnlyList<Token> tokens) {
        bool declares = false;

        foreach (Token token in tokens) {
            if (token.Type == TokenType.Declaration) {
                declares = true;
                continue;
            }

            if (token.Type != TokenType.Text || !string.IsNullOrWhiteSpace(token.Value)) {
                return false;
            }
        }

        return declares;
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
        result.Append(NullableEnable);
        result.Append(Environment.NewLine);
        result.Append($"#line {token.Position.Line} \"{TemplateName}\"");
        result.Append(Environment.NewLine);
        result.Append(' ', token.Position.Column - 1);
        result.Append(token.Value);
        result.Append(Environment.NewLine);
        result.Append("#line default");
        result.Append(Environment.NewLine);
        result.Append(NullableDisable);
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

        // Both mapped regions in a write - the opener's `ctx.Write(` and the expression - belong to
        // the page, so one enable covers them and one disable closes them.
        result.Append(NullableEnable);
        result.Append(Environment.NewLine);
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
        result.Append(NullableDisable);
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
