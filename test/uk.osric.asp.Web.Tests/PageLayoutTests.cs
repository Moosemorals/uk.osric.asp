// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Text.RegularExpressions;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Web.Html;

[TestFixture]
public sealed partial class PageLayoutTests {

    [TestCase("", 1)]
    [TestCase("a", 1)]
    [TestCase("a\n", 2)]
    [TestCase("a\nb", 2)]
    [TestCase("a\r\nb", 2)]
    [TestCase("a\rb", 2)]
    [TestCase("\n\n", 3)]
    public void GutterRendersOneNumberPerSourceLine(string source, int lines) {
        string expected = "<div class=\"gutter\" id=\"gutter\" aria-hidden=\"true\">"
            + string.Concat(Enumerable.Range(1, lines).Select(number => $"<span id=\"L{number}\">{number}</span>"))
            + "</div>";

        Assert.That(PageLayout.Gutter(TemplateLines.Count(source)), Is.EqualTo(expected));
    }

    [Test]
    public void DiagnosticsEncodesTheMessageAndLinksAPositionedOne() {
        PageDiagnostic[] diagnostics = [
            new(PageDiagnosticSeverity.Error, new TemplatePosition(2, 5), "CS0103: <name> does not exist"),
        ];

        Assert.That(PageLayout.Diagnostics(DiagnosticLine.From(diagnostics), linkToLines: true), Is.EqualTo(
            "<ul class=\"diagnostics\" role=\"list\"><li class=\"diagnostic-error\"><a href=\"#L2\">"
            + "line 2, column 5: error CS0103: &lt;name&gt; does not exist</a></li></ul>"));
    }

    [Test]
    public void DiagnosticsLeavesAnUnpositionedOneUnlinked() {
        PageDiagnostic[] diagnostics = [
            new(PageDiagnosticSeverity.Error, null, "PAGE001: no entry point"),
        ];

        Assert.That(PageLayout.Diagnostics(DiagnosticLine.From(diagnostics), linkToLines: true), Is.EqualTo(
            "<ul class=\"diagnostics\" role=\"list\"><li class=\"diagnostic-error\">"
            + "error PAGE001: no entry point</li></ul>"));
    }

    [Test]
    public void DiagnosticsIsByteForByteTheBlockTheErrorPagesRender() {
        PageDiagnostic[] diagnostics = [
            new(PageDiagnosticSeverity.Warning, new TemplatePosition(1, 9), "CS0219: unused"),
        ];

        IReadOnlyList<DiagnosticLine> lines = DiagnosticLine.From(diagnostics);

        Assert.That(PageLayout.Diagnostics(lines), Is.EqualTo(PageLayout.Diagnostics(lines, linkToLines: false)));
        Assert.That(PageLayout.Diagnostics(lines), Does.Not.Contain("<a "));
    }

    [Test]
    public void DiagnosticsStaysInLineOrder() {
        PageDiagnostic[] diagnostics = [
            new(PageDiagnosticSeverity.Error, new TemplatePosition(9, 1), "CS0103: later"),
            new(PageDiagnosticSeverity.Error, new TemplatePosition(2, 1), "CS1002: earlier"),
        ];

        string block = PageLayout.Diagnostics(DiagnosticLine.From(diagnostics), linkToLines: true);

        Assert.That(block.IndexOf("CS1002", StringComparison.Ordinal), Is.LessThan(block.IndexOf("CS0103", StringComparison.Ordinal)));
    }

    [Test]
    public void EveryDiagnosticLinkTargetsALineTheGutterRenders() {
        string gutter = PageLayout.Gutter(40);
        string diagnostics = PageLayout.Diagnostics(DiagnosticLine.From([
            new(PageDiagnosticSeverity.Error, new TemplatePosition(2, 5), "CS0103: missing"),
            new(PageDiagnosticSeverity.Warning, new TemplatePosition(40, 1), "CS0219: unused"),
        ]), linkToLines: true);
        MatchCollection targets = LineLinks().Matches(diagnostics);

        Assert.That(targets, Is.Not.Empty);
        foreach (Match target in targets) {
            Assert.That(gutter, Does.Contain($"id=\"{target.Groups[1].Value}\""), target.Groups[1].Value);
        }
    }

    [GeneratedRegex("href=\"#(L[0-9]+)\"")]
    private static partial Regex LineLinks();
}
