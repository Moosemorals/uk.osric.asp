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

        Assert.That(tokens, Is.EqualTo(new Token[] { new(TokenType.Text, input) }));
    }

    [TestCase("", "<%%>")]
    [TestCase("Write(42);", "<%Write(42);%>")]
    [TestCase(" \r\nWrite(42);\n ", "<% \r\nWrite(42);\n %>")]
    [TestCase("<%nested", "<%<%nested%>")]
    public void CodeBlockExcludesDelimitersAndPreservesContent(string expected, string input) {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize(input).Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] { new(TokenType.Code, expected) }));
    }

    [Test]
    public void AlternatingTextAndCodeRemainInSourceOrder() {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize("before<%first();%>between<%second();%>after").Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Text, "before"),
            new(TokenType.Code, "first();"),
            new(TokenType.Text, "between"),
            new(TokenType.Code, "second();"),
            new(TokenType.Text, "after"),
        }));
    }

    [Test]
    public void AdjacentCodeBlocksDoNotIntroduceEmptyTextTokens() {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize("<%first();%><%%><%second();%>").Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Code, "first();"),
            new(TokenType.Code, ""),
            new(TokenType.Code, "second();"),
        }));
    }

    [Test]
    public void TextBeforeAFinalCodeBlockHasNoEmptyTrailingToken() {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize("before<%code%>").Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Text, "before"),
            new(TokenType.Code, "code"),
        }));
    }

    [TestCase("\"", "\";")]
    [TestCase("// ", "\n")]
    [TestCase("/* ", " */")]
    public void ClosingDelimiterEndsCodeEvenInsideCSharpStringsOrComments(string before, string after) {
        IReadOnlyList<Token> tokens = TemplateTokenizer.Tokenize("<%" + before + "%>" + after).Tokens;

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Code, before),
            new(TokenType.Text, after),
        }));
    }

    [TestCase("<%", "line 1, column 1")]
    [TestCase("<%>", "line 1, column 1")]
    [TestCase("before<%code", "line 1, column 7")]
    [TestCase("<%code%", "line 1, column 1")]
    [TestCase("<%complete%><%unfinished", "line 1, column 13")]
    [TestCase("first line\r\nsecond <% broken", "line 2, column 8")]
    public void UnterminatedCodeBlockIsReportedAtItsOpeningDelimiter(string input, string position) {
        TokenizeResult result = TemplateTokenizer.Tokenize(input);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Is.EqualTo([
            $"TEMPLATE001: unterminated code block; the '<%' at {position} has no matching '%>'.",
        ]));
    }

    [Test]
    public void TokensBeforeAnUnterminatedCodeBlockAreKept() {
        TokenizeResult result = TemplateTokenizer.Tokenize("<%complete%><%unfinished");

        Assert.That(result.Tokens, Is.EqualTo(new Token[] { new(TokenType.Code, "complete") }));
    }
}
