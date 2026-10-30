// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.UnitTests;

using Microsoft.CodeAnalysis.Emit;
using NUnit.Framework;
using System.Text;
using uk.osric.asp.Core;

[TestFixture]
public sealed class TemplateSourceGeneratorTests {
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

    private static TestContext CompileAndRun(string template) {
        string source = TemplateSourceGenerator.GenerateTemplateSource(template);
        using MemoryStream assembly = new();
        EmitResult result = TemplateCompiler.Compile(source, assembly);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));

        assembly.Position = 0;
        TestContext context = new();
        TemplateCompiler.Run(assembly, context);
        return context;
    }

    private sealed class TestContext : IContext {
        private readonly StringBuilder _output = new();

        public string Path => "test";

        public string Output => _output.ToString();

        public void Log(string message) { }

        public void WriteRaw(string message) {
            _output.Append(message);
        }

        public void Write(string message) {
            _output.Append(message);
        }
    }
}