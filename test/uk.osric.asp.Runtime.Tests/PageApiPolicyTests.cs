// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;

[TestFixture]
public sealed class PageApiPolicyTests {

    private static readonly TemplateCompiler Compiler = new();

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
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic.Message.Contains(api, StringComparison.Ordinal)));
    }

    [TestCase("<% } public static void Other() { } %>")]
    [TestCase("<% } } class Extra { } %>")]
    [TestCase("<% } public static class Statics { } %>")]
    public void DeclaringMembersIsRejected(string template) {
        CompileResult result = Compile(template);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic.Message.StartsWith("PAGE002:", StringComparison.Ordinal)));
    }

    [Test]
    public void RejectionReportsThePageLineAndColumn() {
        CompileResult result = Compile("first line\n<% System.IO.File.ReadAllText(\"page.txt\"); %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic.Position is { Line: 2, Column: > 0 }
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
            diagnostic => diagnostic.Message.Contains("System.Object", StringComparison.Ordinal)));
    }

    [Test]
    public void DynamicIsRejected() {
        CompileResult result = Compile("<% dynamic value = 1; ctx.Write(value.ToString()); %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic.Message.Contains("dynamic", StringComparison.Ordinal)));
    }

    private static CompileResult Compile(string template) {
        return Compiler.Compile(Generate(template));
    }

    private static string Generate(string template) {
        TemplateParseResult parsed = TemplateSourceGenerator.GenerateTemplateSource(template);
        Assert.That(parsed.Success, Is.True, string.Join(Environment.NewLine, parsed.Errors));

        // The assertion above proves source was generated.
        return parsed.Source!;
    }
}
