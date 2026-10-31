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
        CompileResult result = Compiler.Compile(TemplateSourceGenerator.GenerateTemplateSource(template));

        Assert.That(result.Success, Is.False);
        // CS0234 rather than CS0246: the assembly is absent, so its namespace is unknown too.
        Assert.That(result.Diagnostics, Has.Some.Contains("error CS02"));
        Assert.That(result.Diagnostics, Has.None.Contains("PAGE001"));
    }

    [Test]
    public void RoslynIsNotReferenced() {
        CompileResult result = Compiler.Compile(
            TemplateSourceGenerator.GenerateTemplateSource(
                "<% Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation = null; %>"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Contains("error CS02"));
        Assert.That(result.Diagnostics, Has.None.Contains("PAGE001"));
    }

    [Test]
    public void SdkTypesAreStillReferenced() {
        CompileResult result = Compiler.Compile(
            TemplateSourceGenerator.GenerateTemplateSource("<% IContext page = ctx; page.Write(page.Path); %>"));

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }
}
