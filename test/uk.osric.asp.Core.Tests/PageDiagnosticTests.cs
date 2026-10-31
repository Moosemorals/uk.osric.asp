// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public sealed class PageDiagnosticTests {

    [Test]
    public void AnErrorIsRenderedWithItsPositionAndCode() {
        PageDiagnostic diagnostic = new(
            PageDiagnosticSeverity.Error,
            new TemplatePosition(2, 16),
            "CS1525: Invalid expression term ';'");

        Assert.That(diagnostic.ToString(),
            Is.EqualTo("line 2, column 16: error CS1525: Invalid expression term ';'"));
    }

    [Test]
    public void AWarningUsesTheWarningWord() {
        PageDiagnostic diagnostic = new(
            PageDiagnosticSeverity.Warning,
            new TemplatePosition(1, 5),
            "CS0219: The variable 'unused' is assigned but its value is never used");

        Assert.That(diagnostic.ToString(),
            Is.EqualTo("line 1, column 5: warning CS0219: The variable 'unused' is assigned but its value is never used"));
    }

    [Test]
    public void AnUnknownPositionOmitsThePosition() {
        PageDiagnostic diagnostic = new(
            PageDiagnosticSeverity.Error,
            null,
            "PAGE002: page code must stay inside a code block.");

        Assert.That(diagnostic.ToString(), Is.EqualTo("error PAGE002: page code must stay inside a code block."));
    }

    [Test]
    public void DisplayOrderIsByLineThenColumn() {
        PageDiagnostic third = new(PageDiagnosticSeverity.Error, new TemplatePosition(3, 1), "third");
        PageDiagnostic secondColumn = new(PageDiagnosticSeverity.Warning, new TemplatePosition(1, 20), "second");
        PageDiagnostic secondLine = new(PageDiagnosticSeverity.Error, new TemplatePosition(2, 9), "second line");
        PageDiagnostic first = new(PageDiagnosticSeverity.Error, new TemplatePosition(1, 4), "first");

        IReadOnlyList<PageDiagnostic> sorted = PageDiagnostic.SortedForDisplay([third, secondColumn, secondLine, first]);

        Assert.That(sorted.Select(diagnostic => diagnostic.Message), Is.EqualTo([
            "first",
            "second",
            "second line",
            "third",
        ]));
    }

    [Test]
    public void DiagnosticsWithoutAPositionSortLast() {
        PageDiagnostic unpositioned = new(PageDiagnosticSeverity.Error, null, "PAGE002");
        PageDiagnostic positioned = new(PageDiagnosticSeverity.Error, new TemplatePosition(2, 1), "CS1525");

        IReadOnlyList<PageDiagnostic> sorted = PageDiagnostic.SortedForDisplay([unpositioned, positioned]);

        Assert.That(sorted.Select(diagnostic => diagnostic.Message), Is.EqualTo(["CS1525", "PAGE002"]));
    }
}
