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
            namespace Site {
                class Helper {
                    public static string Read() => System.IO.File.ReadAllText("library.txt");
                }
            }
            %>
            """);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("System.IO.File.ReadAllText", StringComparison.Ordinal)
                && diagnostic.Position is { Line: 4, Column: > 0 }));
    }

    [Test]
    public void ANullableWarningInALibraryIsMappedToItsLine() {
        CompileResult result = Check(
            "<%! namespace Site { class Helper { public static string Name(IContext ctx) => ctx.User.Name; } } %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Position is { Line: 1, Column: > 0 }));
    }

    [Test]
    public void ALibraryOutsideANamespaceIsRejected() {
        CompileResult result = Check("<%! class Document { } %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message == "PAGE002: a library must declare a namespace, so a page can name it with a using."));
    }

    [Test]
    public void APageCallsTheLibraryItsUsingNames() {
        CompileResult result = CompilePage(
            "<%! using Site; %><% ctx.Write(Document.Head()); %>",
            ("/_lib/document",
                "<%! namespace Site { public static class Document { public static string Head() => \"hi\"; } } %>"));

        Assert.That(result.IsSuccess, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.That(result.Libraries, Is.EqualTo<IEnumerable<string>>(["/_lib/document"]));
        Assert.That(result.Image, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ALibrarysOwnUsingPullsInAnother() {
        CompileResult result = CompilePage(
            "<%! using Site.More; %><% ctx.Write(More.Text()); %>",
            ("/_lib/base",
                "<%! namespace Site.Base { public static class Base { public static string Text() => \"b\"; } } %>"),
            ("/_lib/more",
                "<%! namespace Site.More { using Site.Base; public static class More { public static string Text() => Base.Text() + \"m\"; } } %>"));

        Assert.That(result.IsSuccess, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.That(result.Libraries, Is.EqualTo<IEnumerable<string>>(["/_lib/base", "/_lib/more"]),
            "the closure is transitive and ordered by key");
    }

    [Test]
    public void ALibraryThePageDoesNotNameIsNeverCompiled() {
        CompileResult result = CompilePage(
            "<% ctx.Write(\"ok\"); %>",
            ("/_lib/broken",
                "<%! namespace Site { class Broken { public static string Read() => System.IO.File.ReadAllText(\"x\"); } } %>"));

        Assert.That(result.IsSuccess, Is.True,
            "a library nobody names cannot break a page, which is why the closure is the page's usings");
        Assert.That(result.Libraries, Is.Empty);
    }

    [Test]
    public void ALibrarysErrorIsReportedUnderItsOwnKey() {
        CompileResult result = CompilePage(
            "<%! using Site; %><% ctx.Write(Document.Read()); %>",
            ("/_lib/document",
                "<%! namespace Site { public static class Document { public static string Read() => System.IO.File.ReadAllText(\"x\"); } } %>"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Source == "/_lib/document"
                && diagnostic.Message.Contains("System.IO.File.ReadAllText", StringComparison.Ordinal)));
    }

    [Test]
    public void AMissingNamespaceIsRoslynsOwnError() {
        CompileResult result = CompilePage(
            "<%! using Site.Missing; %><% ctx.Write(\"x\"); %>",
            ("/_lib/document",
                "<%! namespace Site { public static class Document { } } %>"));

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.StartsWith("CS0246:", StringComparison.Ordinal)));
    }

    private static CompileResult CompilePage(string template, params (string Key, string Template)[] libraries) {
        List<LibrarySource> sources = [];
        foreach ((string key, string source) in libraries) {
            sources.Add(new LibrarySource(key, TemplateSourceGenerator.GenerateLibrary(source).Source!));
        }

        TemplateGenerationResult page = TemplateSourceGenerator.Generate(template);

        return Compiler.Compile(page.Source!, sources);
    }

    private static CompileResult Check(string template) {
        TemplateGenerationResult generated = TemplateSourceGenerator.GenerateLibrary(template);

        return Compiler.CompileLibrary(generated.Source!);
    }
}
