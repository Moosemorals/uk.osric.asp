// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using uk.osric.asp.Core.Compilation;

[TestFixture]
public sealed class JavaScriptValidatorTests {

    private const string LargeModule = """
        import { format } from "./format.js";

        export function makePage() {
            const rows = [];
            for (let index = 0; index < 100; index++) {
                rows.push(format(index));
            }
            return rows;
        }
        """;

    [Test]
    public void AValidModuleProducesNoDiagnostics() {
        Assert.That(JavaScriptValidator.Validate(LargeModule), Is.Empty);
    }

    [Test]
    public void AnEmptyScriptIsAccepted() {
        Assert.That(JavaScriptValidator.Validate(string.Empty), Is.Empty);
    }

    [TestCase("import \"./missing.js\";", TestName = "ImportsAreNotResolved")]
    [TestCase("var a = 1;\nvar a = 2;", TestName = "DuplicateVarIsAccepted")]
    [TestCase("doThing();\ndoThing();", TestName = "DuplicatedStatementsAreAccepted")]
    public void SyntacticallyValidSourceProducesNoDiagnostics(string script) {
        Assert.That(JavaScriptValidator.Validate(script), Is.Empty);
    }

    [TestCase("function f() {\nexport const a = 1;\n", "UnexpectedToken", 2, 1)]
    [TestCase("return 1;", "IllegalReturn", 1, 1)]
    [TestCase("function f() {}\nfunction f() {}", "VarRedeclaration", 2, 10)]
    [TestCase("const a = 1;\n}", "UnexpectedToken", 2, 1)]
    [TestCase("const a = { x: 1 y: 2 };", "UnexpectedTokenIdentifier", 1, 18)]
    [TestCase("const a = `x${1};", "UnexpectedEOS", 1, 18)]
    public void ASyntaxErrorIsReportedWithItsCodeAndPagePosition(
        string script,
        string code,
        int line,
        int column) {
        IReadOnlyList<PageDiagnostic> diagnostics = JavaScriptValidator.Validate(script);

        PageDiagnostic error = diagnostics.Single(item => item.Severity == PageDiagnosticSeverity.Error);
        Assert.Multiple(() => {
            Assert.That(error.Message, Does.StartWith($"SCRIPT001: {code}: "));
            Assert.That(error.Position, Is.EqualTo(new TemplatePosition(line, column)));
        });
    }

    [Test]
    public void AHalfFunctionEditAlsoReportsTheOpenerItLeftBehind() {
        const string script = "export function go() {\n    export const answer = 42;\n";

        IReadOnlyList<PageDiagnostic> diagnostics = JavaScriptValidator.Validate(script);

        PageDiagnostic hint = diagnostics.Single(item => item.Severity == PageDiagnosticSeverity.Warning);
        Assert.Multiple(() => {
            Assert.That(hint.Message, Is.EqualTo("SCRIPT002: unclosed '{'."));
            Assert.That(hint.Position, Is.EqualTo(new TemplatePosition(1, 22)));
            Assert.That(diagnostics[0], Is.EqualTo(hint), "the opener is the useful line, so it sorts first");
        });
    }

    [Test]
    public void ATemplateSubstitutionDoesNotCloseAnOuterBrace() {
        const string script = "function f() {\n    const label = `${1}`;\n    export const a = 1;\n";

        IReadOnlyList<PageDiagnostic> diagnostics = JavaScriptValidator.Validate(script);

        Assert.That(
            diagnostics.Where(item => item.Severity == PageDiagnosticSeverity.Warning).Select(item => item.Message),
            Is.EqualTo<string[]>(["SCRIPT002: unclosed '{'."]));
    }

    [Test]
    public void ABalancedTemplateLiteralAndRegularExpressionProduceNoHints() {
        const string script = "const a = `{ } ${ `[ ${1} ]` }`;\nconst b = /[{(]/;\nexport { a, b };\n";

        Assert.That(JavaScriptValidator.Validate(script), Is.Empty);
    }

    [Test]
    public void NoMoreThanTwentyOpenersAreReportedEarliestFirst() {
        string script = string.Concat(Enumerable.Repeat("{\n", 25));

        IReadOnlyList<PageDiagnostic> hints = [
            .. JavaScriptValidator.Validate(script).Where(item => item.Severity == PageDiagnosticSeverity.Warning),
        ];

        Assert.Multiple(() => {
            Assert.That(hints, Has.Count.EqualTo(20));
            Assert.That(hints.Select(hint => hint.Position!.Value.Line), Is.EqualTo(Enumerable.Range(1, 20)));
        });
    }

    [Test]
    public void ANullScriptIsRejected() {
        Assert.Throws<ArgumentNullException>(() => JavaScriptValidator.Validate(null!));
    }
}
