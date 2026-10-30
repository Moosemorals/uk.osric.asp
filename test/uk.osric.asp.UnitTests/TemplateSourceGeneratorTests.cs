// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.UnitTests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public sealed class TemplateSourceGeneratorTests {
    [Test]
    public void TextBecomesAWriteRawCall() {
        string source = TemplateSourceGenerator.GenerateTemplateSource("Hello");

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"Hello\");"));
    }

    [Test]
    public void QuotesInTextAreDoubledForAVerbatimLiteral() {
        string source = TemplateSourceGenerator.GenerateTemplateSource("Quote: \"Hello\"\\path");

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"Quote: \"\"Hello\"\"\\path\");"));
    }

    [Test]
    public void NewlinesInTextArePreserved() {
        string source = TemplateSourceGenerator.GenerateTemplateSource("first\r\nsecond");

        Assert.That(source, Does.Contain("@\"first\r\nsecond\""));
    }

    [Test]
    public void CodeBlocksAreCopiedVerbatimBetweenText() {
        string source = TemplateSourceGenerator.GenerateTemplateSource("before<% ctx.Write(\"middle\"); %>after");

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"before\");"));
        Assert.That(source, Does.Contain("ctx.Write(\"middle\");"));
        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"after\");"));
    }

    [Test]
    public void GeneratedSourceDeclaresAHandler() {
        string source = TemplateSourceGenerator.GenerateTemplateSource("");

        Assert.That(source, Does.Contain("using uk.osric.asp.Core;"));
        Assert.That(source, Does.Contain("class GeneratedPage : IHandler"));
        Assert.That(source, Does.Contain("void Handle(IContext ctx)"));
    }
}
