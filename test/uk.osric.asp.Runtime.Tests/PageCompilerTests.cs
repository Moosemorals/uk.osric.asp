// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using NUnit.Framework;
using System.Diagnostics;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;

[TestFixture]
public sealed class PageCompilerTests {

    private static readonly PageCompiler Compiler = new();

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
    public void LiteralTextWithHtmlMetacharactersIsPreserved() {
        const string template = "<svg title=\"A & B > C\"></svg>";

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
        // NUnit types the Matches predicate's item as nullable, so check it before reading it.
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS1525", StringComparison.Ordinal)
                && diagnostic.Position == new TemplatePosition(1, 16)));
    }

    [Test]
    public void ASyntaxErrorReportsItsTemplateLineAndColumn() {
        CompileResult result = Compile("line one\nline two\n<% int value = ; %>");

        PageDiagnostic error = result.Diagnostics.Single(diagnostic =>
            diagnostic.Message.Contains("CS1525", StringComparison.Ordinal));

        Assert.That(error.Severity, Is.EqualTo(PageDiagnosticSeverity.Error));
        Assert.That(error.Position, Is.EqualTo(new TemplatePosition(3, 16)));
    }

    [Test]
    public void AWriteExpressionErrorOnALaterLineReportsItsTemplatePosition() {
        CompileResult result = Compile("first\n<%=\n    ctx\n      .Missing\n%>");

        PageDiagnostic error = result.Diagnostics.Single(diagnostic =>
            diagnostic.Message.Contains("CS1061", StringComparison.Ordinal));

        Assert.That(error.Position, Is.EqualTo(new TemplatePosition(4, 8)));
    }

    [Test]
    public void AWriteExpressionMustHaveAStringType() {
        CompileResult result = Compile("<%= 42 %>");

        PageDiagnostic error = result.Diagnostics.Single(diagnostic =>
            diagnostic.Message.Contains("CS1503", StringComparison.Ordinal));

        Assert.That(error.Position, Is.EqualTo(new TemplatePosition(1, 5)));
    }

    [Test]
    public void AWriteCallDiagnosticReportsItsTemplateLineAndCallColumn() {
        CompileResult result = Compile("\n  <%=\n    ctx.Path, ctx.Path %>");

        PageDiagnostic error = result.Diagnostics.Single(diagnostic =>
            diagnostic.Message.Contains("CS1501", StringComparison.Ordinal));

        Assert.That(error.Position, Is.EqualTo(new TemplatePosition(2, 7)));
    }

    [Test]
    public void AWarningOnlyPageSucceedsAndEmitsAnImage() {
        CompileResult result = Compile("<% int unused = 1; %>");

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Symbols, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("CS0219", StringComparison.Ordinal)));
    }

    [Test]
    public void AWarningDoesNotHideAnError() {
        CompileResult result = Compile("<% int unused = 1; int value = \"text\"; %>");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Error));
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("CS0219", StringComparison.Ordinal)));
    }

    [Test]
    public void AnUnclosedBlockIsReportedAtTheEndOfTheTemplate() {
        CompileResult result = Compile("<% if (true) { %>");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS1513", StringComparison.Ordinal)
                && diagnostic.Position == new TemplatePosition(2, 3)));
    }

    [Test]
    public void AGeneratedSourceFaultThrowsInsteadOfReturningADiagnostic() {
        const string source = """
            using uk.osric.asp.Core;
            public class GeneratedPage : IHandler {
            public void Handle(IContext ctx) {
            int value = ;
            }}
            """;

        Assert.Throws<InvalidOperationException>(() => Compiler.Compile(new GeneratedSource(source)));
    }

    [Test]
    public void AGeneratedSourcePolicyViolationThrowsInsteadOfReturningADiagnostic() {
        const string source = """
            using uk.osric.asp.Core;
            public class GeneratedPage : IHandler {
            public void Handle(IContext ctx) {
            System.IO.File.ReadAllText("page.txt");
            }}
            """;

        Assert.Throws<InvalidOperationException>(() => Compiler.Compile(new GeneratedSource(source)));
    }

    [Test]
    public void AMappedErrorSuppressesAnUnmappedCascade() {
        const string source = """
            using uk.osric.asp.Core;
            public class GeneratedPage : IHandler {
            public void Handle(IContext ctx) {
            #line 1 "page"
            int value = ;
            #line default
            this is not valid;
            }}
            """;

        CompileResult result = Compiler.Compile(new GeneratedSource(source));

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS1525", StringComparison.Ordinal)
                && diagnostic.Position?.Line == 1));
    }

    [Test]
    public void AUsingBlockAfterATypeBlockIsReportedInTemplateSpace() {
        CompileResult result = Compile("<%! class Amount { } %>\n<%! using System.Globalization; %>");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS1529", StringComparison.Ordinal)
                && diagnostic.Position is { Line: 2, Column: > 0 }));
    }

    [Test]
    public void AnErrorInsideADeclaredTypeReportsItsTemplateLineAndColumn() {
        CompileResult result = Compile("""
            first
            <%!
            class Amount {
                public static int Value() => "text";
            }
            %>
            """);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS0029", StringComparison.Ordinal)
                && diagnostic.Position == new TemplatePosition(4, 34)));
    }

    [Test]
    public void APageDeclaredTypeIsUsableFromPageCode() {
        TestContext context = CompileAndRun("""
            <%!
            sealed class Amount {
                public static string Format(decimal value) => value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
            }
            %>
            Total: <% ctx.Write(Amount.Format(42.5m)); %>
            """);

        Assert.That(context.Output, Is.EqualTo("\nTotal: 42.50"));
    }

    [Test]
    public void APageDeclaredTypeIsUsableFromMoreThanOneBlock() {
        TestContext context = CompileAndRun("""
            <%!static class Greeting { public static string For(string who) => "Hello, " + who; }%>
            <% ctx.Write(Greeting.For("world")); %><% ctx.Write(Greeting.For("again")); %>
            """);

        Assert.That(context.Output, Is.EqualTo("\nHello, worldHello, again"));
    }

    [Test]
    public void APageDeclaredHandlerIsNotTheEntryPoint() {
        TestContext context = CompileAndRun("""
            <%!
            sealed class Decoy : uk.osric.asp.Sdk.IHandler {
                public void Handle(uk.osric.asp.Sdk.IContext ctx) => ctx.Write("decoy");
            }
            %>
            real
            """);

        Assert.That(context.Output, Does.Not.Contain("decoy"));
        Assert.That(context.Output, Does.Contain("real"));
    }

    [Test]
    public void AnUnmappedDeclarationPolicyViolationThrowsInsteadOfReturningADiagnostic() {
        const string source = """
            class Helper {
                public static string Read() => System.IO.File.ReadAllText("page.txt");
            }
            public class GeneratedPage : uk.osric.asp.Sdk.IHandler {
            public void Handle(uk.osric.asp.Sdk.IContext ctx) {
            }}
            """;

        Assert.Throws<InvalidOperationException>(() => Compiler.Compile(new GeneratedSource(source)));
    }

    [Test]
    public void ValidSourceEmitsAnImage() {
        CompileResult result = Compile("Hello.");

        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.That(result.Image, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void OneImageCanBeLoadedAndInvokedMoreThanOnce() {
        using LoadedPage template = Load("Hello, <% ctx.Write(\"world\"); %>!");
        TestContext first = new();
        TestContext second = new();

        template.Invoke(first);
        template.Invoke(second);

        Assert.That(first.Output, Is.EqualTo("Hello, world!"));
        Assert.That(second.Output, Is.EqualTo("Hello, world!"));
    }

    [Test]
    public void TemplateExceptionsSurfaceUnwrapped() {
        using LoadedPage template = Load("line one\nline two\n<% throw new System.InvalidOperationException(\"Template executed.\"); %>");

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => template.Invoke(new TestContext()));

        Assert.That(failure?.Message, Is.EqualTo("Template executed."));
        Assert.That(failure?.StackTrace, Does.Contain("GeneratedPage.Handle"));
        StackFrame? templateFrame = new StackTrace(failure, true).GetFrames()
            ?.SingleOrDefault(frame => frame.GetFileName() == "page");
        Assert.That(templateFrame, Is.Not.Null);
        Assert.That(templateFrame!.GetFileLineNumber(), Is.EqualTo(3));
        Assert.That(templateFrame.GetFileColumnNumber(), Is.EqualTo(4));
    }

    [Test]
    public void RuntimeIdStartsWithTheFormatAndPolicyVersions() {
        Assert.That(PageCompiler.RuntimeId, Does.StartWith("4:1:"));
    }

    [Test]
    public void RuntimeIdContainsTheSdkAndCoreModuleVersionIds() {
        string runtimeId = PageCompiler.RuntimeId;

        Assert.That(runtimeId,
            Does.Contain(typeof(IHandler).Assembly.ManifestModule.ModuleVersionId.ToString("N")));
        Assert.That(runtimeId,
            Does.Contain(typeof(PageDiagnostic).Assembly.ManifestModule.ModuleVersionId.ToString("N")));
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

    private static LoadedPage Load(string template) {
        CompileResult result = Compile(template);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));

        // The assertion above proves the image exists when compilation succeeded.
        return Compiler.Load(result.Image!, result.Symbols);
    }

    private static TestContext CompileAndRun(string template) {
        using LoadedPage page = Load(template);
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
