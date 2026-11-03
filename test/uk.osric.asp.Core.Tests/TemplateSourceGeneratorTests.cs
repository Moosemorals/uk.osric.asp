// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public sealed class TemplateSourceGeneratorTests {
    [Test]
    public void TextBecomesAWriteRawCall() {
        string? source = TemplateSourceGenerator.Generate("Hello").Source;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"Hello\");"));
    }

    [Test]
    public void QuotesInTextAreDoubledForAVerbatimLiteral() {
        string? source = TemplateSourceGenerator.Generate("Quote: \"Hello\"\\path").Source;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"Quote: \"\"Hello\"\"\\path\");"));
    }

    [Test]
    public void NewlinesInTextArePreserved() {
        string? source = TemplateSourceGenerator.Generate("first\r\nsecond").Source;

        Assert.That(source, Does.Contain("@\"first\r\nsecond\""));
    }

    [Test]
    public void CodeBlocksAreCopiedVerbatimBetweenText() {
        string? source = TemplateSourceGenerator.Generate("before<% ctx.Write(\"middle\"); %>after").Source;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"before\");"));
        Assert.That(source, Does.Contain("ctx.Write(\"middle\");"));
        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"after\");"));
    }

    [Test]
    public void GeneratedSourceDeclaresAHandler() {
        string? source = TemplateSourceGenerator.Generate("").Source;

        Assert.That(source, Does.Contain("using uk.osric.asp.Sdk;"));
        Assert.That(source, Does.Contain("class GeneratedPage : IHandler"));
        Assert.That(source, Does.Contain("void Handle(IContext ctx)"));
    }

    [Test]
    public void AnUnterminatedCodeBlockProducesNoSourceAndOneError() {
        TemplateParseResult result = TemplateSourceGenerator.Generate("before<% if (true) {");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Source, Is.Null);
        Assert.That(result.Errors, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(1, 7),
                "TEMPLATE001: unterminated code block; the '<%' has no matching '%>'."),
        }));
    }

    [Test]
    public void AnUnterminatedCodeBlockIsReportedOnItsOwnTemplateLine() {
        TemplateParseResult result = TemplateSourceGenerator.Generate("line one\nline two <% if (true) {");

        // NUnit types the Matches predicate's item as nullable, so check it before reading it.
        Assert.That(result.Errors, Has.Exactly(1).Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Position == new TemplatePosition(2, 10)));
    }

    [Test]
    public void ACodeBlockIsMappedToItsTemplateLineAndResetAfterwards() {
        string source = Generate("first\n<% int value = 1; %>");

        Assert.That(source, Does.Contain($"#line 2 \"page\"{Environment.NewLine}"));
        Assert.That(source, Does.Contain($"#line default{Environment.NewLine}"));
    }

    [Test]
    public void CodeIsPaddedToItsTemplateColumn() {
        string source = Generate("first\n<% int value = 1; %>");

        Assert.That(source, Does.Contain("   int value = 1;"));
    }

    [Test]
    public void EachCodeBlockKeepsItsOwnTemplateLine() {
        string source = Generate("alpha\nbeta\n<% int first = 1; %>\ngamma\ndelta\n<% int second = 2; %>");

        Assert.That(source, Does.Contain("#line 3 \"page\""));
        Assert.That(source, Does.Contain("#line 6 \"page\""));
    }

    [Test]
    public void ThePostludeIsMappedToOneLinePastTheTemplate() {
        string source = Generate("alpha\nbeta");

        Assert.That(source, Does.Contain("#line 3 \"page\"" + Environment.NewLine + "}}"));
    }

    private static string Generate(string template) {
        TemplateParseResult result = TemplateSourceGenerator.Generate(template);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));

        // The assertion above proves source was generated.
        return result.Source!;
    }

    [Test]
    public void AParsedTemplateReportsNoErrors() {
        TemplateParseResult result = TemplateSourceGenerator.Generate("Hello");

        Assert.That(result.Success, Is.True);
        Assert.That(result.Errors, Is.Empty);
    }
}
