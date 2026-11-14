// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class TemplateSourceGeneratorTests {
    [Test]
    public void TextBecomesAWriteRawCall() {
        string? source = TemplateSourceGenerator.Generate("Hello").Source?.Text;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"Hello\");"));
    }

    [Test]
    public void QuotesInTextAreDoubledForAVerbatimLiteral() {
        string? source = TemplateSourceGenerator.Generate("Quote: \"Hello\"\\path").Source?.Text;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"Quote: \"\"Hello\"\"\\path\");"));
    }

    [Test]
    public void NewlinesInTextArePreserved() {
        string? source = TemplateSourceGenerator.Generate("first\r\nsecond").Source?.Text;

        Assert.That(source, Does.Contain("@\"first\r\nsecond\""));
    }

    [Test]
    public void CodeBlocksAreCopiedVerbatimBetweenText() {
        string? source = TemplateSourceGenerator.Generate("before<% ctx.Write(\"middle\"); %>after").Source?.Text;

        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"before\");"));
        Assert.That(source, Does.Contain("ctx.Write(\"middle\");"));
        Assert.That(source, Does.Contain("ctx.WriteRaw(@\"after\");"));
    }

    [TestCase("<%= ctx.Path %>", "ctx.Write(", "ctx.Path")]
    [TestCase("<%== ctx.Path %>", "ctx.WriteRaw(", "ctx.Path")]
    public void WriteBlocksGenerateTheMatchingContextCall(string template, string call, string expression) {
        string source = Generate(template);

        Assert.That(source, Does.Contain($"{call}{Environment.NewLine}"));
        Assert.That(source, Does.Contain($"{expression}{Environment.NewLine}#line default{Environment.NewLine}#nullable disable{Environment.NewLine});"));
    }

    [Test]
    public void WriteExpressionWhitespaceIsTrimmedButInternalTextIsPreserved() {
        string source = Generate("<%=   ctx.Path +\n      ctx.Path   %>");

        Assert.That(source, Does.Contain(
            $"#line 1 \"page\"{Environment.NewLine}ctx.Write({Environment.NewLine}"
            + $"#line 1 \"page\"{Environment.NewLine}      ctx.Path +\n      ctx.Path"));
    }

    [TestCase("<%= ctx.Path; %>", "ctx.Write(", "ctx.Path")]
    [TestCase("<%== ctx.Path; %>", "ctx.WriteRaw(", "ctx.Path")]
    public void OneTrailingSemicolonIsRemoved(string template, string call, string expression) {
        string source = Generate(template);

        Assert.That(source, Does.Contain($"{call}{Environment.NewLine}"));
        Assert.That(source, Does.Contain($"{expression}{Environment.NewLine}#line default{Environment.NewLine}#nullable disable{Environment.NewLine});"));
    }

    [Test]
    public void OnlyOneTrailingSemicolonIsRemoved() {
        string source = Generate("<%== ctx.Path; ; %>");

        Assert.That(source, Does.Contain($"ctx.Path; {Environment.NewLine}#line default{Environment.NewLine}#nullable disable{Environment.NewLine});"));
    }

    [Test]
    public void AWriteExpressionAfterLeadingNewlinesMapsToItsTemplateColumn() {
        string source = Generate("<%=\r\n  ctx.Path\r\n%>");

        Assert.That(source, Does.Contain(
            $"#line 2 \"page\"{Environment.NewLine}  ctx.Path{Environment.NewLine}#line default{Environment.NewLine}"));
    }

    [Test]
    public void AWriteWrapperMapsToItsOpenerAndTheExpressionMapsToItsContent() {
        string source = Generate("  <%=\n    ctx.Path %>");

        Assert.That(source, Does.Contain(
            $"#line 1 \"page\"{Environment.NewLine}  ctx.Write({Environment.NewLine}"
            + $"#line 2 \"page\"{Environment.NewLine}    ctx.Path{Environment.NewLine}#line default{Environment.NewLine}#nullable disable{Environment.NewLine});"));
    }

    [TestCase("<%=   %>", 1, 1)]
    [TestCase("<%== ; %>", 1, 1)]
    [TestCase("before\n  <%= ; %>", 2, 3)]
    public void AnEmptyWriteExpressionProducesNoSourceAndOneDiagnostic(
        string template,
        int line,
        int column) {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate(template);

        Assert.That(result.Source, Is.Null);
        Assert.That(result.Errors, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(line, column),
                "TEMPLATE002: write expression is empty."),
        }));
    }

    [Test]
    public void GeneratedSourceDeclaresAHandler() {
        string? source = TemplateSourceGenerator.Generate("").Source?.Text;

        Assert.That(source, Does.Contain("using uk.osric.asp.Sdk;"));
        Assert.That(source, Does.Contain("class GeneratedPage : uk.osric.asp.Sdk.IHandler"));
        Assert.That(source, Does.Contain("Task HandleAsync(uk.osric.asp.Sdk.IContext ctx)"));
    }

    [Test]
    public void TheHandlerNamesSdkTypesFullyQualified() {
        string source = Generate("");

        Assert.That(source, Does.Contain("public class GeneratedPage : uk.osric.asp.Sdk.IHandler {"));
        Assert.That(source, Does.Contain("public async global::System.Threading.Tasks.Task HandleAsync(uk.osric.asp.Sdk.IContext ctx) {"));
    }

    [Test]
    public void AnEmptyTemplateStillGeneratesAHandler() {
        string source = Generate("");

        Assert.That(source, Does.Contain("HandleAsync"));
        Assert.That(source, Does.Not.Contain("ctx.WriteRaw"));
    }

    [Test]
    public void TheAsyncMethodSuppressesTheNoAwaitWarning() {
        string source = Generate("Hello");

        int disable = source.IndexOf("#pragma warning disable CS1998", StringComparison.Ordinal);
        int signature = source.IndexOf("HandleAsync", StringComparison.Ordinal);

        Assert.That(disable, Is.GreaterThan(-1).And.LessThan(signature));
        Assert.That(source, Does.Contain("#pragma warning restore CS1998"));
    }

    [Test]
    public void DeclarationsAreEmittedBeforeTheHandler() {
        string source = Generate("<%! class Amount { } %>Hello");

        int declaration = source.IndexOf("class Amount { }", StringComparison.Ordinal);
        int handler = source.IndexOf("class GeneratedPage", StringComparison.Ordinal);

        Assert.That(declaration, Is.GreaterThan(-1));
        Assert.That(handler, Is.GreaterThan(declaration));
    }

    [Test]
    public void DeclarationBlocksAreMappedToTheirTemplateLineAndColumn() {
        string source = Generate("first\n<%!class Amount { }%>");

        Assert.That(source, Does.Contain($"#line 2 \"page\"{Environment.NewLine}   class Amount {{ }}"));
        Assert.That(source, Does.Contain($"#line default{Environment.NewLine}"));
    }

    [Test]
    public void DeclarationBlocksDoNotWriteOutput() {
        string source = Generate("<%! class Amount { } %>");

        Assert.That(source, Does.Not.Contain("ctx.WriteRaw"));
    }

    [Test]
    public void MultipleDeclarationBlocksKeepTheirOrder() {
        string source = Generate("<%! class Amount { } %><%! class Formatter { } %>");

        int first = source.IndexOf("class Amount { }", StringComparison.Ordinal);
        int second = source.IndexOf("class Formatter { }", StringComparison.Ordinal);

        Assert.That(first, Is.GreaterThan(-1));
        Assert.That(second, Is.GreaterThan(first));
    }

    [Test]
    public void TextAroundDeclarationBlocksKeepsItsOutputOrder() {
        string source = Generate("before<%! class Amount { } %>middle<%! class Formatter { } %>after");

        int amount = source.IndexOf("class Amount { }", StringComparison.Ordinal);
        int formatter = source.IndexOf("class Formatter { }", StringComparison.Ordinal);
        int before = source.IndexOf("ctx.WriteRaw(@\"before\");", StringComparison.Ordinal);
        int middle = source.IndexOf("ctx.WriteRaw(@\"middle\");", StringComparison.Ordinal);
        int after = source.IndexOf("ctx.WriteRaw(@\"after\");", StringComparison.Ordinal);

        Assert.That(amount, Is.GreaterThan(-1).And.LessThan(before));
        Assert.That(formatter, Is.GreaterThan(amount).And.LessThan(before));
        Assert.That(before, Is.LessThan(middle));
        Assert.That(middle, Is.LessThan(after));
    }

    [Test]
    public void ADeclarationBlockInsideAConditionalIsStillHoisted() {
        string source = Generate("<% if (true) { %><%! class Amount { } %><% } %>");

        int declaration = source.IndexOf("class Amount { }", StringComparison.Ordinal);
        int conditional = source.IndexOf("if (true) {", StringComparison.Ordinal);

        Assert.That(declaration, Is.GreaterThan(-1));
        Assert.That(conditional, Is.GreaterThan(declaration));
    }

    [Test]
    public void AnUnterminatedDeclarationBlockProducesNoSourceAndOneError() {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate("before<%! class Amount {");

        Assert.That(result.Source, Is.Null);
        Assert.That(result.Errors, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(1, 7),
                "TEMPLATE001: unterminated declaration block; the '<%!' has no matching '%>'."),
        }));
    }

    [Test]
    public void AnUnterminatedCodeBlockProducesNoSourceAndOneError() {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate("before<% if (true) {");

        Assert.That(result.Source, Is.Null);
        Assert.That(result.Errors, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(1, 7),
                "TEMPLATE001: unterminated code block; the '<%' has no matching '%>'."),
        }));
    }

    [Test]
    public void AnUnterminatedCodeBlockIsReportedOnItsOwnTemplateLine() {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate("line one\nline two <% if (true) {");

        // NUnit types the Matches predicate's item as nullable, so check it before reading it.
        Assert.That(result.Errors, Has.Exactly(1).Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Position == new TemplatePosition(2, 10)));
    }

    [Test]
    public void ACodeBlockIsMappedToItsTemplateLineAndResetAfterwards() {
        string source = Generate("first\n<% int value = 1; %>");

        Assert.That(source, Does.Contain($"#line 2 \"page\"{Environment.NewLine}"));
        Assert.That(source, Does.Contain($"#line default{Environment.NewLine}"));
    }

    [Test]
    public void CodeIsPaddedToItsTemplateColumn() {
        string source = Generate("first\n<% int value = 1; %>");

        Assert.That(source, Does.Contain("   int value = 1;"));
    }

    [Test]
    public void EachCodeBlockKeepsItsOwnTemplateLine() {
        string source = Generate("alpha\nbeta\n<% int first = 1; %>\ngamma\ndelta\n<% int second = 2; %>");

        Assert.That(source, Does.Contain("#line 3 \"page\""));
        Assert.That(source, Does.Contain("#line 6 \"page\""));
    }

    [Test]
    public void AwaitInsideAWriteExpressionStaysInsideTheWriteCall() {
        string source = Generate("<%= await ctx.Path.ToStringAsync() %>");

        Assert.That(source, Does.Contain(
            $"ctx.Write({Environment.NewLine}"
            + $"#line 1 \"page\"{Environment.NewLine}"));
        Assert.That(source, Does.Contain(
            $"await ctx.Path.ToStringAsync(){Environment.NewLine}#line default{Environment.NewLine}#nullable disable{Environment.NewLine});"));
    }

    [Test]
    public void ThePostludeIsMappedToOneLinePastTheTemplate() {
        string source = Generate("alpha\nbeta");

        Assert.That(source, Does.Contain("#line 3 \"page\"" + Environment.NewLine + "}}"));
    }

    [Test]
    public void AGuardWithAReturnStaysInsideTheGeneratedHandler() {
        string source = Generate(
            "<% if (ctx.User is null) { ctx.RedirectToLogin(); return; } %>after");

        int handler = source.IndexOf("HandleAsync", StringComparison.Ordinal);
        int guard = source.IndexOf("return;", StringComparison.Ordinal);
        int after = source.IndexOf("ctx.WriteRaw(@\"after\");", StringComparison.Ordinal);

        Assert.That(handler, Is.GreaterThanOrEqualTo(0));
        Assert.That(guard, Is.GreaterThan(handler), "the guard must be emitted inside the handler");
        Assert.That(after, Is.GreaterThan(guard), "the rest of the page still follows the guard");
    }

    private static string Generate(string template) {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate(template);
        Assert.That(result.Source, Is.Not.Null, string.Join(Environment.NewLine, result.Errors));

        return result.Source!.Text;
    }

    [Test]
    public void AParsedTemplateReportsNoErrors() {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate("Hello");

        Assert.That(result.Source, Is.Not.Null);
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void TheScaffoldStartsNullableOblivious() {
        string source = Generate("Hello");

        Assert.That(source, Does.StartWith("#nullable disable" + Environment.NewLine));
    }

    [Test]
    public void TheNullableDirectiveIsWrittenOutsideTheMappedRegion() {
        string source = Generate("first\n<% int value = 1; %>");

        // Inside a region, `#line` would map the directive's own line onto the author's, moving every
        // position after it. Outside, the directive's line is the generator's own.
        Assert.That(source, Does.Contain(
            $"#nullable enable{Environment.NewLine}#line 2 \"page\"{Environment.NewLine}"));
        Assert.That(source, Does.Contain(
            $"#line default{Environment.NewLine}#nullable disable{Environment.NewLine}"));
    }

    [Test]
    public void APageThatDisablesNullableForItselfKeepsItDisabled() {
        string source = Generate("<%\n#nullable disable\nctx.Write(ctx.User.Name);\n%>");

        // The generator's directive is the one before the region; the page's own is the first line of
        // its code, copied through untouched. Inside the region the last directive wins, which is how
        // a page that wants to be oblivious says so.
        Assert.That(source, Does.Contain(
            $"#nullable enable{Environment.NewLine}#line 1 \"page\"{Environment.NewLine}"
            + $"  {Environment.NewLine}#nullable disable{Environment.NewLine}ctx.Write(ctx.User.Name);"));
    }
}

[TestFixture]
public sealed class TemplateSourceGeneratorKindTests {

    [TestCase("")]
    [TestCase("Hello")]
    [TestCase("<p>Just text.</p>")]
    [TestCase("a <!-- comment --> b")]
    [TestCase("\n\n\t ")]
    public void ATextOnlyTemplateIsText(string template) {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate(template);

        Assert.That(result.Kind, Is.EqualTo(PageKind.Text));
        Assert.That(result.Source, Is.Not.Null);
    }

    [TestCase("<% int value = 1; %>")]
    [TestCase("<%= ctx.Path %>")]
    [TestCase("<%== ctx.Path %>")]
    [TestCase("<%! class Amount { } %><% ctx.Write(\"x\"); %>")]
    [TestCase("<%! class Amount { } %>x")]
    public void ACodeTemplateThatRendersIsAPage(string template) {
        Assert.That(TemplateSourceGenerator.Generate(template).Kind, Is.EqualTo(PageKind.Page));
    }

    [TestCase("<%! class Amount { } %>")]
    [TestCase("<%! using System.Text; %>")]
    [TestCase("<%! using System.Text; %>\n  <%! class Amount { } %>\r\n")]
    public void ADeclarationOnlyTemplateIsALibrary(string template) {
        Assert.That(TemplateSourceGenerator.Generate(template).Kind, Is.EqualTo(PageKind.Library));
    }

    [Test]
    public void ACommentBesideADeclarationIsMarkupAndTheTemplateIsAPage() {
        Assert.That(
            TemplateSourceGenerator.Generate("<%! class Amount { } %><!-- note -->").Kind,
            Is.EqualTo(PageKind.Page));
    }

    [Test]
    public void AParseFailureIsAPage() {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate("before<% if (true) {");

        Assert.That(result.Source, Is.Null);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(result.Kind, Is.EqualTo(PageKind.Page), "a template the tokenizer refused must not be served as an asset");
    }

    [Test]
    public void AnEmptyWriteIsAPage() {
        TemplateGenerationResult result = TemplateSourceGenerator.Generate("<%= %>");

        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(result.Kind, Is.EqualTo(PageKind.Page));
    }
}
