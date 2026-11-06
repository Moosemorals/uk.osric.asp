// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the compiler's reference set keeps the shell out of reach.</summary>
/// <remarks>
/// These tests can only live in the web test project: naming a shell type is the whole point, and
/// only this project references the shell assemblies.
/// </remarks>
[TestFixture]
public sealed class PageScopeTests {

    private static readonly PageCompiler Compiler = new();

    [TestCase("<% uk.osric.asp.Web.SimpleContext context = null; %>")]
    [TestCase("<% _ = new uk.osric.asp.Web.Program(); %>")]
    [TestCase("<% uk.osric.asp.Runtime.PageCompiler compiler = null; %>")]
    [TestCase("<% uk.osric.asp.Core.PagePath path = null; %>")]
    public void ShellTypesAreNotReferenced(string template) {
        CompileResult result = Compiler.Compile(Generate(template));

        Assert.That(result.IsSuccess, Is.False);
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

        Assert.That(result.IsSuccess, Is.False);
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

        Assert.That(result.IsSuccess, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Test]
    public async Task AwaitedPageCodeRendersItsValue() {
        string template = """
            <%!
            using System.Threading.Tasks;

            static class Slow {
                public static async Task<string> ValueAsync() {
                    await Task.CompletedTask;
                    return "awaited";
                }
            }
            %>
            <% ctx.Write(await Slow.ValueAsync()); %>|<%= await Slow.ValueAsync() %>
            """;

        using PageHarness harness = new(new FakePageStore());
        await harness.StoreCompiledAsync("/awaited", template);

        (RenderResult result, string output) = await harness.RenderAsync("/awaited");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("awaited"));
        Assert.That(output, Does.Contain("|awaited"));
    }

    [Test]
    public void TheStorageAndFetchContractCompiles() {
        string template = """
            <%
                PageValue<int> stored = await ctx.Storage.TryGetValueAsync<int>("visits");
                int visits = stored.Found ? stored.Value : 0;
                await ctx.Storage.SetAsync("visits", visits + 1);
                await ctx.Storage.DeleteAsync("gone");
                ctx.Write(System.String.Join(",", await ctx.Storage.KeysAsync()));

                PageFetchResponse response = await ctx.FetchAsync(new PageFetchRequest("https://example.com/"));
                ctx.Write(response.Succeeded ? response.Body : response.Failure.ToString());
            %>
            """;

        CompileResult result = Compiler.Compile(Generate(template));

        Assert.That(result.IsSuccess, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
    }

    private static GeneratedSource Generate(string template) {
        TemplateGenerationResult parsed = TemplateSourceGenerator.Generate(template);
        Assert.That(parsed.IsSuccess, Is.True, string.Join(Environment.NewLine, parsed.Errors));

        // The assertion above proves source was generated.
        return parsed.Source!;
    }
}
