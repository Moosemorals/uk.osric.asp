// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Compilation;

[TestFixture]
public sealed class TemplateShapeCheckTests {

    [Test]
    public void ABalancedBlockAcrossMarkupHasNoWarnings() {
        IReadOnlyList<PageDiagnostic> diagnostics =
            TemplateShapeCheck.Check("<% if (true) { %>text<% } %>");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void AnUnclosedBraceWarnsAtItsOpener() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<% if (true) { %>text");

        Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
            diagnostic is not null
            && diagnostic.Severity == PageDiagnosticSeverity.Warning
            && diagnostic.Position?.Line == 1
            && diagnostic.Message.StartsWith("TEMPLATE004:", StringComparison.Ordinal)));
    }

    [Test]
    public void AnExtraClosingBraceWarns() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<% } %>");

        Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
            diagnostic is not null
            && diagnostic.Severity == PageDiagnosticSeverity.Warning
            && diagnostic.Message.StartsWith("TEMPLATE005:", StringComparison.Ordinal)));
    }

    [Test]
    public void AStrayCloserInTextWarnsWhereItAppears() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("first\n100%> done");

        Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
            diagnostic is not null
            && diagnostic.Severity == PageDiagnosticSeverity.Warning
            && diagnostic.Position?.Line == 2
            && diagnostic.Message.StartsWith("TEMPLATE003:", StringComparison.Ordinal)));
    }

    [Test]
    public void BracesInStringsCommentsAndCharactersAreIgnored() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check(
            "<% string text = \"}\"; char brace = '{'; /* } */ // }\nint value = 1; %>");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void BracesInVerbatimAndRawStringsAreIgnored() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check(
            "<% string verbatim = @\"}\"; string raw = \"\"\"{ }\"\"\"; %>");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void BracesInInterpolatedStringsAreIgnored() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check(
            "<% string text = $\"{new Item { Name = \"x\" }.Name}\"; string braces = $\"{{ }}\"; %>");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void AnUnterminatedBlockIsLeftToTheTokenizer() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<% if (true) {");

        Assert.That(diagnostics, Is.Empty, "the tokenizer already reports the missing '%>'");
    }

    [Test]
    public void AWarningIsPositionedInTemplateSpace() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check(
            "line one\n<% if (true) { %>text");

        Assert.That(diagnostics.Single().Position?.Line, Is.EqualTo(2));
    }
}
