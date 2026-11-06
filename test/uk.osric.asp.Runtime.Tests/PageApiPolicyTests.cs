// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;

[TestFixture]
public sealed class PageApiPolicyTests {

    private static readonly PageCompiler Compiler = new();

    [TestCase("<% System.IO.File.ReadAllText(\"page.txt\"); %>", "System.IO.File.ReadAllText")]
    [TestCase("<% System.Environment.Exit(0); %>", "System.Environment.Exit")]
    [TestCase("<% System.Diagnostics.Process.Start(\"sh\"); %>", "System.Diagnostics.Process.Start")]
    [TestCase("<% _ = typeof(string); %>", "System.Type")]
    [TestCase("<% _ = ctx.GetType(); %>", "System.Object.GetType")]
    [TestCase("<% System.Span<int> values = stackalloc int[4]; %>", "stackalloc")]
    [TestCase("<% System.AppDomain.CurrentDomain.GetAssemblies(); %>", "System.AppDomain")]
    public void BannedCodeIsRejectedWithoutAnImage(string template, string api) {
        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Image, Is.Null);
        // NUnit types the Matches predicate's item as nullable, so check it before reading it.
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains(api, StringComparison.Ordinal)));
    }

    [TestCase("<% } public static void Other() { } %>")]
    [TestCase("<% } } class Extra { } %>")]
    [TestCase("<% } public static class Statics { } %>")]
    public void DeclaringMembersIsRejected(string template) {
        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.StartsWith("PAGE002:", StringComparison.Ordinal)));
    }

    [Test]
    public void ADeclarationBlockMayDeclareAType() {
        string template = """
            <%!
            using System.Globalization;

            sealed class Amount {
                public static string Format(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
            }
            %>
            <p>Total: <% ctx.Write(Amount.Format(42.5m)); %></p>
            """;

        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestCase("<%! int value = 1; %>")]
    [TestCase("<%! System.Console.WriteLine(\"escaped\"); %>")]
    [TestCase("<%! if (true) { } %>")]
    public void AStatementInADeclarationBlockIsRejected(string template) {
        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.StartsWith("PAGE002:", StringComparison.Ordinal)
                && diagnostic.Position is { Line: 1, Column: > 0 }));
    }

    [Test]
    public void ANamespaceInADeclarationBlockIsRejected() {
        CompileResult result = Compile("<%! namespace Page { class Amount { } } %>");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message == "PAGE002: a page may not declare a namespace."
                && diagnostic.Position is { Line: 1, Column: > 0 }));
    }

    [Test]
    public void APageMayNotDeclareGeneratedPage() {
        CompileResult result = Compile("<%! class GeneratedPage { } %>");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message == "PAGE002: a page may not declare its own GeneratedPage."));
    }

    [Test]
    public void AnAssemblyAttributeIsRejected() {
        CompileResult result = Compile("<%![assembly: System.Reflection.AssemblyTitle(\"page\")]%>");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message == "PAGE002: a page may not add assembly attributes."));
    }

    [Test]
    public void ABannedApiInsideADeclaredTypeIsRejectedAtItsTemplatePosition() {
        string template = """
            <%!
            class Helper {
                public static string Read() => System.IO.File.ReadAllText("page.txt");
            }
            %>
            """;

        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Image, Is.Null);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("System.IO.File.ReadAllText", StringComparison.Ordinal)
                && diagnostic.Position is { Line: 3, Column: > 0 }));
    }

    [Test]
    public void AUsingDirectiveDoesNotBypassThePolicy() {
        string template = """
            <%!
            using System.IO;

            class Helper {
                public static string Read() => File.ReadAllText("page.txt");
            }
            %>
            """;

        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("System.IO.File.ReadAllText", StringComparison.Ordinal)
                && diagnostic.Position is { Line: 5, Column: > 0 }));
    }

    [Test]
    public void APageDeclaredTypeMayUseAnAllowedBclType() {
        string template = """
            <%!
            using System.Collections.Generic;
            using System.Globalization;
            using System.Text;

            sealed class Report {
                private readonly StringBuilder _text = new();
                private readonly List<int> _values = [];

                public void Add(int value) => _values.Add(value);

                public string Format() {
                    foreach (int value in _values) {
                        _text.Append(value.ToString(CultureInfo.InvariantCulture));
                    }

                    return _text.ToString();
                }
            }
            %>
            <% Report report = new(); report.Add(1); ctx.Write(report.Format()); %>
            """;

        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Test]
    public void ADeclarationBlockThatOnlyImportsANamespaceCompiles() {
        CompileResult result = Compile("<%! using System.Globalization; %>");

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Test]
    public void RejectionReportsThePageLineAndColumn() {
        CompileResult result = Compile("first line\n<% System.IO.File.ReadAllText(\"page.txt\"); %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Position is { Line: 2, Column: > 0 }
                && diagnostic.Severity == PageDiagnosticSeverity.Error));
    }

    [Test]
    public void OrdinaryPageCodeCompiles() {
        string template = """
            <%
                System.Collections.Generic.List<int> items = [1, 2, 3];
                foreach (int item in System.Linq.Enumerable.Where(items, value => value > 1)) {
                    ctx.Write($"{item} ");
                }

                ctx.Write(System.String.Join(",", items));
                try {
                    throw new System.InvalidOperationException("caught");
                } catch (System.InvalidOperationException error) {
                    ctx.Write(error.Message);
                }
            %>
            """;

        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Test]
    public void CallingAnObjectMemberIsRejected() {
        CompileResult result = Compile("<% System.Object value = 1; ctx.Write(value.ToString()); %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("System.Object", StringComparison.Ordinal)));
    }

    [Test]
    public void DynamicIsRejected() {
        CompileResult result = Compile("<% dynamic value = 1; ctx.Write(value.ToString()); %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("dynamic", StringComparison.Ordinal)));
    }

    private static CompileResult Compile(string template) {
        return Compiler.Compile(Generate(template));
    }

    private static GeneratedSource Generate(string template) {
        TemplateGenerationResult parsed = TemplateSourceGenerator.Generate(template);
        Assert.That(parsed.Success, Is.True, string.Join(Environment.NewLine, parsed.Errors));

        // The assertion above proves source was generated.
        return parsed.Source!;
    }
}
