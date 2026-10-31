// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public sealed class TemplateSourceGeneratorTests {
    [Test]
    public void TextBecomesAWriteRawCall() {
        string? source = TemplateSourceGenerator.GenerateTemplateSource("Hello").Source;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"Hello\");"));
    }

    [Test]
    public void QuotesInTextAreDoubledForAVerbatimLiteral() {
        string? source = TemplateSourceGenerator.GenerateTemplateSource("Quote: \"Hello\"\\path").Source;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"Quote: \"\"Hello\"\"\\path\");"));
    }

    [Test]
    public void NewlinesInTextArePreserved() {
        string? source = TemplateSourceGenerator.GenerateTemplateSource("first\r\nsecond").Source;

        Assert.That(source, Does.Contain("@\"first\r\nsecond\""));
    }

    [Test]
    public void CodeBlocksAreCopiedVerbatimBetweenText() {
        string? source = TemplateSourceGenerator.GenerateTemplateSource("before<% ctx.Write(\"middle\"); %>after").Source;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"before\");"));
        Assert.That(source, Does.Contain("ctx.Write(\"middle\");"));
        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"after\");"));
    }

    [Test]
    public void GeneratedSourceDeclaresAHandler() {
        string? source = TemplateSourceGenerator.GenerateTemplateSource("").Source;

        Assert.That(source, Does.Contain("using uk.osric.asp.Core;"));
        Assert.That(source, Does.Contain("class GeneratedPage : IHandler"));
        Assert.That(source, Does.Contain("void Handle(IContext ctx)"));
    }

    [Test]
    public void AnUnterminatedCodeBlockProducesNoSourceAndOneError() {
        TemplateParseResult result = TemplateSourceGenerator.GenerateTemplateSource("before<% if (true) {");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Source, Is.Null);
        Assert.That(result.Errors, Is.EqualTo([
            "TEMPLATE001: unterminated code block; the '<%' at line 1, column 7 has no matching '%>'.",
        ]));
    }

    [Test]
    public void AnUnterminatedCodeBlockIsReportedOnItsOwnTemplateLine() {
        TemplateParseResult result = TemplateSourceGenerator.GenerateTemplateSource("line one\nline two <% if (true) {");

        Assert.That(result.Errors, Has.Exactly(1).Contains("'<%' at line 2, column 10"));
    }

    [Test]
    public void AParsedTemplateReportsNoErrors() {
        TemplateParseResult result = TemplateSourceGenerator.GenerateTemplateSource("Hello");

        Assert.That(result.Success, Is.True);
        Assert.That(result.Errors, Is.Empty);
    }
}
