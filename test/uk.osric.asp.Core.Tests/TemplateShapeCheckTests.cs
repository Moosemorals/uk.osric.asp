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

    [TestCase("<%# { %>")]
    [TestCase("<%# } %>")]
    [TestCase("<%# a note about { and } %>")]
    [TestCase("text<%# { %>more")]
    public void BracesInACommentAreNotThePagesBraces(string template) {
        Assert.That(TemplateShapeCheck.Check(template), Is.Empty);
    }

    [Test]
    public void ACommentDoesNotCloseABraceThePageOpened() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<% if (true) { %>text<%# } %>");

        Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
            diagnostic is not null
            && diagnostic.Message.StartsWith("TEMPLATE004:", StringComparison.Ordinal)));
    }

    [TestCase("<% %>")]
    [TestCase("<%    %>")]
    [TestCase("<%\n\n  %>")]
    public void ABlockThatHoldsNoCodeWarnsAndSaysWhatItMadeOfTheTemplate(string template) {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check(template);

        PageDiagnostic warning = diagnostics.Single(diagnostic => diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE006:", StringComparison.Ordinal));
        Assert.Multiple(() => {
            Assert.That(warning.Severity, Is.EqualTo(PageDiagnosticSeverity.Warning));
            Assert.That(warning.Message, Does.Contain("what makes this template a page"));
        });
    }

    [Test]
    public void ADeadBlockBesideRealCodeWarnsWithoutTheConsequence() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<% int x = 1; %><% %>");

        PageDiagnostic warning = diagnostics.Single(diagnostic => diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE006:", StringComparison.Ordinal));
        Assert.That(warning.Message, Does.Not.Contain("makes this template"));
    }

    [Test]
    public void ABlockThatHoldsOnlyACommentGetsItsOwnWarning() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<% /* note */ %>");

        PageDiagnostic warning = diagnostics.Single(diagnostic => diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE007:", StringComparison.Ordinal));
        Assert.Multiple(() => {
            Assert.That(diagnostics, Has.None.Matches<PageDiagnostic>(diagnostic =>
                diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE006:", StringComparison.Ordinal)));
            Assert.That(warning.Message, Does.Contain("<%#"));
            Assert.That(warning.Message, Does.Contain("what makes this template a page"));
        });
    }

    [Test]
    public void ADeclarationThatHoldsOnlyACommentSaysItMadeALibrary() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<%! /* note */ %>");

        PageDiagnostic warning = diagnostics.Single(diagnostic => diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE007:", StringComparison.Ordinal));
        Assert.That(warning.Message, Does.Contain("what makes this template a library, which is never served"));
    }

    [TestCase("<% int x = 1; %>")]
    [TestCase("<% ; %>")]
    [TestCase("<%# a note %>")]
    [TestCase("<%= \"x\" %>")]
    public void ABlockThatHoldsSomethingIsNotWarnedAbout(string template) {
        Assert.That(TemplateShapeCheck.Check(template), Is.Empty);
    }

    [TestCase("<% string s = \"%>\"; %>")]
    [TestCase("<% string v = @\"%>\"; %>")]
    [TestCase("<% string i = $\"%>\"; %>")]
    [TestCase("<% string r = \"\"\"%>\"; %>")]
    public void ABlockCutShortInsideALiteralIsDiagnosedWhereItStarts(string template) {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check(template);

        Assert.Multiple(() => {
            Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
                diagnostic is not null
                && diagnostic.Message.StartsWith("TEMPLATE008:", StringComparison.Ordinal)
                && diagnostic.Position?.Line == 1
                && diagnostic.Message.Contains("\\u003e", StringComparison.Ordinal)));
            Assert.That(diagnostics, Has.None.Matches<PageDiagnostic>(diagnostic =>
                diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE003:", StringComparison.Ordinal)),
                "the closer that ended the block is the one the author meant");
        });
    }

    [Test]
    public void ABlockCutShortInsideACommentWarnsToCloseTheComment() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<% /* note %>");

        PageDiagnostic warning = diagnostics.Single(diagnostic => diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE008:", StringComparison.Ordinal));
        Assert.That(warning.Message, Does.Contain("*/"));
    }

    [Test]
    public void ALaterStrayCloserAfterACutBlockIsStillReported() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<% s = \"%>\"; t = \"%>\" %>");

        Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
            diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE008:", StringComparison.Ordinal)));
        Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
            diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE003:", StringComparison.Ordinal)));
    }

    [Test]
    public void ADeadBlockIsPositionedAtItsOpenerEvenWhenItsContentIsOnAnotherLine() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check("<%\n/* note */%>");

        PageDiagnostic warning = diagnostics.Single(diagnostic => diagnostic is not null && diagnostic.Message.StartsWith("TEMPLATE007:", StringComparison.Ordinal));
        Assert.Multiple(() => {
            Assert.That(warning.Position?.Line, Is.EqualTo(1));
            Assert.That(warning.Position?.Column, Is.EqualTo(1));
        });
    }

    [Test]
    public void AWarningIsPositionedInTemplateSpace() {
        IReadOnlyList<PageDiagnostic> diagnostics = TemplateShapeCheck.Check(
            "line one\n<% if (true) { %>text");

        Assert.That(diagnostics.Single().Position?.Line, Is.EqualTo(2));
    }
}
