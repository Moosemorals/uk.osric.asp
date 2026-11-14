// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

[TestFixture]
public sealed class PageLibraryTests {

    private static readonly PageCompiler Compiler = new();

    [Test]
    public void ANamespaceAndATypeCompile() {
        CompileResult result = Check("""
            <%!
            namespace Site.Document {
                public static class Document {
                    public static string Head() => "<head></head>";
                }
            }
            %>
            """);

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Image, Is.Null, "a library is checked, never emitted");
    }

    [Test]
    public void AStatementInALibraryIsRejectedAtItsPosition() {
        CompileResult result = Check("<%! System.Console.WriteLine(\"x\"); %>");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.StartsWith("PAGE002:", StringComparison.Ordinal)
                && diagnostic.Position is { Line: 1, Column: > 0 }));
    }

    [Test]
    public void ALibraryMayNotDeclareGeneratedPage() {
        CompileResult result = Check("<%! class GeneratedPage { } %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message == "PAGE002: a library may not declare GeneratedPage."));
    }

    [Test]
    public void ANamespacedGeneratedPageIsAllowed() {
        CompileResult result = Check("<%! namespace Site { class GeneratedPage { } } %>");

        Assert.That(result.IsSuccess, Is.True, "only a global GeneratedPage could collide with a dependent's handler");
    }

    [Test]
    public void AnAssemblyAttributeIsRejected() {
        CompileResult result = Check("<%![assembly: System.Reflection.AssemblyTitle(\"library\")]%>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message == "PAGE002: a library may not add assembly attributes."));
    }

    [Test]
    public void ALibraryThatDeclaresNoTypeIsRejected() {
        CompileResult result = Check("<%! using System.Text; %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message == "PAGE002: a library must declare a type."));
    }

    [Test]
    public void ABannedApiIsReportedAtTheLibrarysOwnLine() {
        CompileResult result = Check("""
            <%!
            class Helper {
                public static string Read() => System.IO.File.ReadAllText("library.txt");
            }
            %>
            """);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("System.IO.File.ReadAllText", StringComparison.Ordinal)
                && diagnostic.Position is { Line: 3, Column: > 0 }));
    }

    [Test]
    public void ANullableWarningInALibraryIsMappedToItsLine() {
        CompileResult result = Check(
            "<%! class Helper { public static string Name(IContext ctx) => ctx.User.Name; } %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Position is { Line: 1, Column: > 0 }));
    }

    private static CompileResult Check(string template) {
        TemplateGenerationResult generated = TemplateSourceGenerator.GenerateLibrary(template);

        return Compiler.CompileLibrary(generated.Source!);
    }
}
