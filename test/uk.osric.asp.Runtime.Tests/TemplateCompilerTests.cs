// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using NUnit.Framework;
using System.Reflection;
using System.Text;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;

[TestFixture]
public sealed class TemplateCompilerTests {

    private static readonly TemplateCompiler Compiler = new();

    [Test]
    public void EmptyTemplateCompilesAndProducesNoOutput() {
        TestContext context = CompileAndRun("");

        Assert.That(context.Output, Is.Empty);
    }

    [Test]
    public void TextWithQuotesSlashesAndNewlinesIsPreserved() {
        string template = "Quote: \"Hello\"\\path\r\n" + "Triple quote: \"\"\"";

        TestContext context = CompileAndRun(template);

        Assert.That(context.Output, Is.EqualTo(template));
    }

    [Test]
    public void CodeBlocksExecuteBetweenText() {
        TestContext context = CompileAndRun("before<% ctx.Write(\"middle\"); %>after");

        Assert.That(context.Output, Is.EqualTo("beforemiddleafter"));
    }

    [Test]
    public void InvalidSourceReportsDiagnosticsWithoutAnImage() {
        CompileResult result = Compile("<% int value = ; %>");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Is.Not.Empty);
        Assert.That(result.Image, Is.Null);
    }

    [Test]
    public void ValidSourceEmitsAnImage() {
        CompileResult result = Compile("Hello.");

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void OneImageCanBeLoadedAndInvokedMoreThanOnce() {
        using CompiledTemplate template = Load("Hello, <% ctx.Write(\"world\"); %>!");
        TestContext first = new();
        TestContext second = new();

        template.Invoke(first);
        template.Invoke(second);

        Assert.That(first.Output, Is.EqualTo("Hello, world!"));
        Assert.That(second.Output, Is.EqualTo("Hello, world!"));
    }

    [Test]
    public void TemplateExceptionsSurfaceUnwrapped() {
        using CompiledTemplate template = Load("<% throw new System.InvalidOperationException(\"Template executed.\"); %>");

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => template.Invoke(new TestContext()));

        Assert.That(failure?.Message, Is.EqualTo("Template executed."));
        Assert.That(failure?.StackTrace, Does.Contain("GeneratedPage.Handle"));
    }

    [Test]
    public void RuntimeIdStartsWithTheFormatAndPolicyVersions() {
        Assert.That(TemplateCompiler.RuntimeId, Does.StartWith("2:1:"));
    }

    [Test]
    public void RuntimeIdContainsTheCoreModuleVersionId() {
        Assert.That(TemplateCompiler.RuntimeId,
            Does.EndWith(typeof(IHandler).Assembly.ManifestModule.ModuleVersionId.ToString("N")));
    }

    [Test]
    public void EveryCompilerInstanceReportsTheSameRuntimeId() {
        ITemplateCompiler other = new TemplateCompiler();

        Assert.That(other.RuntimeId, Is.EqualTo(TemplateCompiler.RuntimeId));
    }

    private static CompileResult Compile(string template) {
        return Compiler.Compile(TemplateSourceGenerator.GenerateTemplateSource(template));
    }

    private static CompiledTemplate Load(string template) {
        CompileResult result = Compile(template);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));

        // The assertion above proves the image exists when compilation succeeded.
        return Compiler.Load(result.Image!);
    }

    private static TestContext CompileAndRun(string template) {
        using CompiledTemplate page = Load(template);
        TestContext context = new();
        page.Invoke(context);
        return context;
    }

    private sealed class TestContext : IContext {
        private static readonly IReadOnlyDictionary<string, string> s_empty = new Dictionary<string, string>();

        private readonly StringBuilder _output = new();

        public PageRequest Request { get; } = new("GET", "test", s_empty, s_empty, s_empty, s_empty);

        public string Path => "test";

        public CancellationToken Aborted => CancellationToken.None;

        public string Output => _output.ToString();

        public void Log(string message) { }

        public void LogWarning(string message) { }

        public void LogError(string message) { }

        public void WriteRaw(string message) {
            _output.Append(message);
        }

        public void Write(string message) {
            _output.Append(message);
        }

        public void SetStatus(int status) { }

        public void SetContentType(string contentType) { }

        public void SetHeader(string name, string value) { }

        public void Redirect(string location, int status = 302) { }
    }
}
