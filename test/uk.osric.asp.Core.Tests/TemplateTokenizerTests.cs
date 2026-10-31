// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public sealed class TemplateTokenizerTests {
    [Test]
    public void EmptyInputProducesNoTokens() {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize("").Tokens;

        Assert.That(tokens, Is.Empty);
    }

    [TestCase("<h1>Hello</h1>")]
    [TestCase(" \t\r\n ")]
    [TestCase("text %> more text")]
    [TestCase("<")]
    [TestCase("< %")]
    [TestCase("text <")]
    public void TextWithoutAnOpeningDelimiterIsPreserved(string input) {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize(input).Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Text, input, new TemplatePosition(1, 1)),
        }));
    }

    [TestCase("", "<%%>")]
    [TestCase("Write(42);", "<%Write(42);%>")]
    [TestCase(" \r\nWrite(42);\n ", "<% \r\nWrite(42);\n %>")]
    [TestCase("<%nested", "<%<%nested%>")]
    public void CodeBlockExcludesDelimitersAndPreservesContent(string expected, string input) {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize(input).Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Code, expected, new TemplatePosition(1, 3)),
        }));
    }

    [Test]
    public void AlternatingTextAndCodeRemainInSourceOrder() {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize("before<%first();%>between<%second();%>after").Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Text, "before", new TemplatePosition(1, 1)),
            new(TokenType.Code, "first();", new TemplatePosition(1, 9)),
            new(TokenType.Text, "between", new TemplatePosition(1, 19)),
            new(TokenType.Code, "second();", new TemplatePosition(1, 28)),
            new(TokenType.Text, "after", new TemplatePosition(1, 39)),
        }));
    }

    [Test]
    public void AdjacentCodeBlocksDoNotIntroduceEmptyTextTokens() {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize("<%first();%><%%><%second();%>").Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Code, "first();", new TemplatePosition(1, 3)),
            new(TokenType.Code, "", new TemplatePosition(1, 15)),
            new(TokenType.Code, "second();", new TemplatePosition(1, 19)),
        }));
    }

    [Test]
    public void TextBeforeAFinalCodeBlockHasNoEmptyTrailingToken() {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize("before<%code%>").Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Text, "before", new TemplatePosition(1, 1)),
            new(TokenType.Code, "code", new TemplatePosition(1, 9)),
        }));
    }

    [TestCase("\"", "\";")]
    [TestCase("// ", "\n")]
    [TestCase("/* ", " */")]
    public void ClosingDelimiterEndsCodeEvenInsideCSharpStringsOrComments(string before, string after) {
        string input = "<%" + before + "%>" + after;
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize(input).Tokens;
        int textStart = input.IndexOf("%>", StringComparison.Ordinal) + 2;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Code, before, new TemplatePosition(1, 3)),
            new(TokenType.Text, after, new TemplatePosition(1, textStart + 1)),
        }));
    }

    [TestCase("<%", 1, 1)]
    [TestCase("<%>", 1, 1)]
    [TestCase("before<%code", 1, 7)]
    [TestCase("<%code%", 1, 1)]
    [TestCase("<%complete%><%unfinished", 1, 13)]
    [TestCase("first line\r\nsecond <% broken", 2, 8)]
    public void UnterminatedCodeBlockIsReportedAtItsOpeningDelimiter(string input, int line, int column) {
        TokenizeResult result = TemplateTokenizer.Tokenize(input);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(line, column),
                "TEMPLATE001: unterminated code block; the '<%' has no matching '%>'."),
        }));
    }

    [Test]
    public void TokensBeforeAnUnterminatedCodeBlockAreKept() {
        TokenizeResult result = TemplateTokenizer.Tokenize("<%complete%><%unfinished");

        Assert.That(result.Tokens, Is.EqualTo(new Token[] {
            new(TokenType.Code, "complete", new TemplatePosition(1, 3)),
        }));
    }
}
