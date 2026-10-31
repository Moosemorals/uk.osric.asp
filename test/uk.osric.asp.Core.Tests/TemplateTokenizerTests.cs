// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public sealed class TemplateTokenizerTests {
    [Test]
    public void EmptyInputProducesNoTokens() {
        Token[] tokens = [.. TemplateTokenizer.Tokenize("")];

        Assert.That(tokens, Is.Empty);
    }

    [TestCase("<h1>Hello</h1>")]
    [TestCase(" \t\r\n ")]
    [TestCase("text %> more text")]
    [TestCase("<")]
    [TestCase("< %")]
    [TestCase("text <")]
    public void TextWithoutAnOpeningDelimiterIsPreserved(string input) {
        Token[] tokens = [.. TemplateTokenizer.Tokenize(input)];

        Assert.That(tokens, Is.EqualTo(new Token[] { new(TokenType.Text, input) }));
    }

    [TestCase("", "<%%>")]
    [TestCase("Write(42);", "<%Write(42);%>")]
    [TestCase(" \r\nWrite(42);\n ", "<% \r\nWrite(42);\n %>")]
    [TestCase("<%nested", "<%<%nested%>")]
    public void CodeBlockExcludesDelimitersAndPreservesContent(string expected, string input) {
        Token[] tokens = [.. TemplateTokenizer.Tokenize(input)];

        Assert.That(tokens, Is.EqualTo(new Token[] { new(TokenType.Code, expected) }));
    }

    [Test]
    public void AlternatingTextAndCodeRemainInSourceOrder() {
        Token[] tokens = [.. TemplateTokenizer.Tokenize("before<%first();%>between<%second();%>after")];

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
        Token[] tokens = [.. TemplateTokenizer.Tokenize("<%first();%><%%><%second();%>")];

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Code, "first();"),
            new(TokenType.Code, ""),
            new(TokenType.Code, "second();"),
        }));
    }

    [Test]
    public void TextBeforeAFinalCodeBlockHasNoEmptyTrailingToken() {
        Token[] tokens = [.. TemplateTokenizer.Tokenize("before<%code%>")];

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Text, "before"),
            new(TokenType.Code, "code"),
        }));
    }

    [TestCase("\"", "\";")]
    [TestCase("// ", "\n")]
    [TestCase("/* ", " */")]
    public void ClosingDelimiterEndsCodeEvenInsideCSharpStringsOrComments(string before, string after) {
        Token[] tokens = [.. TemplateTokenizer.Tokenize("<%" + before + "%>" + after)];

        Assert.That(tokens, Is.EqualTo(new Token[] {
            new(TokenType.Code, before),
            new(TokenType.Text, after),
        }));
    }

    [TestCase("<%")]
    [TestCase("<%>")]
    [TestCase("before<%code")]
    [TestCase("<%code%")]
    [TestCase("<%complete%><%unfinished")]
    public void UnterminatedCodeBlockThrowsWhenEnumerated(string input) {
        Assert.That(() => TemplateTokenizer.Tokenize(input).ToArray(),
            Throws.TypeOf<ArgumentException>().With.Message.EqualTo("Unterminated code block."));
    }
}
