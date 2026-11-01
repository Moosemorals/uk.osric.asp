// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;

/// <summary>Proves the compiler's reference set keeps the shell out of reach.</summary>
/// <remarks>
/// These tests can only live in the web test project: naming a shell type is the whole point, and
/// only this project references the shell assemblies.
/// </remarks>
[TestFixture]
public sealed class PageScopeTests {

    private static readonly TemplateCompiler Compiler = new();

    [TestCase("<% uk.osric.asp.Web.SimpleContext context = null; %>")]
    [TestCase("<% _ = new uk.osric.asp.Web.Program(); %>")]
    [TestCase("<% uk.osric.asp.Runtime.TemplateCompiler compiler = null; %>")]
    public void ShellTypesAreNotReferenced(string template) {
        CompileResult result = Compiler.Compile(Generate(template));

        Assert.That(result.Success, Is.False);
        // CS0234 rather than CS0246: the assembly is absent, so its namespace is unknown too.
        // NUnit types the Matches predicate's item as nullable, so check it before reading it.
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS02", StringComparison.Ordinal)));
        Assert.That(result.Diagnostics, Has.None.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("PAGE001", StringComparison.Ordinal)));
    }

    [Test]
    public void RoslynIsNotReferenced() {
        CompileResult result = Compiler.Compile(
            Generate("<% Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation = null; %>"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS02", StringComparison.Ordinal)));
        Assert.That(result.Diagnostics, Has.None.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("PAGE001", StringComparison.Ordinal)));
    }

    [Test]
    public void SdkTypesAreStillReferenced() {
        CompileResult result = Compiler.Compile(
            Generate("<% IContext page = ctx; page.Write(page.Path); %>"));

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static string Generate(string template) {
        TemplateParseResult parsed = TemplateSourceGenerator.GenerateTemplateSource(template);
        Assert.That(parsed.Success, Is.True, string.Join(Environment.NewLine, parsed.Errors));

        // The assertion above proves source was generated.
        return parsed.Source!;
    }
}
