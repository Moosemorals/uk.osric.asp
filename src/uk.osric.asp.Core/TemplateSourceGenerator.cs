// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using Microsoft.CodeAnalysis.CSharp;
using System.Text;

public static class TemplateSourceGenerator {

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
                        string literal = SyntaxFactory.Literal(token.Value).ToString();
                        result.Append("ctx.WriteRaw(");
                        result.Append(literal);
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
}
