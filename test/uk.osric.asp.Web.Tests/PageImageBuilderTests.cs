// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the template → compilation step, without storage in the way.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// The two deadline tests measure a wall clock, so the fixture wants the process to itself.
[NonParallelizable]
public sealed class PageImageBuilderTests {

    [Test]
    public void BuildReturnsAnImageWithNoDiagnostics() {
        using PageHarness pages = new(new FakePageStore());

        PageImage compilation = Build(pages, "/hello", "<% ctx.Write(\"Hello.\"); %>");

        Assert.That(compilation.Diagnostics, Is.Empty);
        Assert.That(compilation.Bytes, Is.Not.Null.And.Not.Empty);
        Assert.That(compilation.Kind, Is.EqualTo(PageKind.Page));
        Assert.That(compilation.TemplateHash, Is.EqualTo(TemplateFingerprint.Of("<% ctx.Write(\"Hello.\"); %>")));
        Assert.That(compilation.RuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
    }

    [Test]
    public void AParseErrorDoesNotReachTheCompiler() {
        using PageHarness pages = new(new FakePageStore());

        PageImage compilation = Build(pages, "/unterminated", "before<% if (true) {");

        Assert.That(compilation.Diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(1, 7),
                "TEMPLATE001: unterminated code block; the '<%' has no matching '%>'."),
        }));
        Assert.That(compilation.Bytes, Is.Null);
        Assert.That(pages.Compiler.CompileCount, Is.Zero, "a template that does not parse never reaches the compiler");
    }

    [Test]
    public void AWarningDoesNotStopThePage() {
        using PageHarness pages = new(new FakePageStore());

        PageImage compilation = Build(pages, "/warned", "<% int unused = 1; %>");

        Assert.That(compilation.Bytes, Is.Not.Null.And.Not.Empty);
        Assert.That(compilation.Diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("CS0219", StringComparison.Ordinal)));
    }

    [Test]
    public void ACompilerFaultPropagates() {
        using PageHarness pages = new(new FakePageStore(), new FaultingCompiler());

        Assert.That(
            () => Build(pages, "/fault", "<% ctx.Write(\"Hello.\"); %>"),
            Throws.InvalidOperationException);
    }

    [Test]
    public void ABuildThatOutranItsBudgetIsReported() {
        PageBudget budget = new(TestOptions.Render, TestOptions.Compile with { TimeoutSeconds = 0.25 });
        using PageHarness pages = new(new FakePageStore(), new StallingCompiler(), budget: budget);

        PageImage compilation = Build(pages, "/stalled", "<% ctx.Write(\"Hello.\"); %>");

        Assert.That(compilation.Diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, null, "PAGE004: the page took longer than 0.25 s to compile."),
        }));
        Assert.That(compilation.Bytes, Is.Null);
    }

    [Test]
    public async Task ACompileThatWaitedPastItsBudgetForASlotIsReportedAsync() {
        PageBudget budget = new(TestOptions.Render, TestOptions.Compile with { TimeoutSeconds = 0.25 });
        using PageHarness pages = new(new FakePageStore(), budget: budget);

        await budget.CompileSlots.WaitAsync();
        try {
            long start = Stopwatch.GetTimestamp();
            PageImage compilation = Build(pages, "/queued", "<% ctx.Write(\"Hello.\"); %>");

            Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromSeconds(1)),
                "the wait for a slot is bounded by the compile budget");
            Assert.That(compilation.Diagnostics, Is.EqualTo(new PageDiagnostic[] {
                new(PageDiagnosticSeverity.Error, null,
                    "PAGE003: the page waited longer than 0.25 s for a compile slot."),
            }));
        } finally {
            budget.CompileSlots.Release();
        }

        Assert.That(pages.Compiler.CompileCount, Is.Zero);
    }

    [Test]
    public void ATemplateWithNoCodeIsStoredWithoutCompiling() {
        using PageHarness pages = new(new FakePageStore());

        PageImage compilation = Build(pages, "/site.css", "body { color: red; }");

        Assert.That(compilation.Kind, Is.EqualTo(PageKind.Text));
        Assert.That(compilation.Bytes, Is.Null);
        Assert.That(compilation.Symbols, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Empty);
        Assert.That(pages.Compiler.CompileCount, Is.Zero, "there was nothing to compile");
    }

    [Test]
    public void ALibraryIsCheckedAndStoredWithoutAnImage() {
        using PageHarness pages = new(new FakePageStore());

        PageImage compilation = Build(pages, "/_lib/document", "<%! namespace Site { class Document { } } %>");

        Assert.Multiple(() => {
            Assert.That(compilation.Kind, Is.EqualTo(PageKind.Library));
            Assert.That(compilation.Bytes, Is.Null, "a library is never run, so there is no assembly");
            Assert.That(compilation.Symbols, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Empty);
            Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1), "a library is checked once");
        });
    }

    [Test]
    public void APageThatNamesALibraryStoresTheFingerprintOfItsClosure() {
        using PageHarness pages = new(new FakePageStore());
        PageLibrary library = new(
            PageHarness.Key("/_lib/document"), "hash-one",
            "<%! namespace Site { public static class Document { public static string Head() => \"hi\"; } } %>");

        PageImage compilation = pages.Images.Build(
            PageHarness.Key("/uses"), "<%! using Site; %><% ctx.Write(Document.Head()); %>",
            PageMetrics.ReasonSave, [library], CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(compilation.Diagnostics, Is.Empty);
            Assert.That(compilation.Bytes, Is.Not.Null.And.Not.Empty);
            Assert.That(compilation.Libraries, Is.EqualTo("/_lib/document@hash-one"));
        });
    }

    [Test]
    public void ALibraryWithABannedApiIsStoredWithItsDiagnostics() {
        using PageHarness pages = new(new FakePageStore());

        PageImage compilation = Build(pages, "/_lib/document",
            "<%! namespace Site { class Helper { public static string Read() => System.IO.File.ReadAllText(\"x\"); } } %>");

        Assert.Multiple(() => {
            Assert.That(compilation.Kind, Is.EqualTo(PageKind.Library));
            Assert.That(compilation.Bytes, Is.Null);
            Assert.That(compilation.Diagnostics, Has.Some.Matches<PageDiagnostic>(diagnostic =>
                diagnostic is not null
                && diagnostic.Message.Contains("System.IO.File.ReadAllText", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void ATemplateShapeWarningIsStoredEvenWhenThePageCompiles() {
        using PageHarness pages = new(new FakePageStore());

        PageImage compilation = Build(pages, "/shape", "<% ctx.Write(\"one\"); %>\n100%> done");

        Assert.Multiple(() => {
            Assert.That(compilation.Bytes, Is.Not.Null.And.Not.Empty);
            Assert.That(compilation.Diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
                diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Message.StartsWith("TEMPLATE003:", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void ANoCodeTemplateCanStillCarryAShapeWarning() {
        using PageHarness pages = new(new FakePageStore());

        PageImage compilation = Build(pages, "/stray", "100%> done");

        Assert.Multiple(() => {
            Assert.That(compilation.Kind, Is.EqualTo(PageKind.Text));
            Assert.That(compilation.Diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(diagnostic =>
                diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Message.StartsWith("TEMPLATE003:", StringComparison.Ordinal)));
        });
    }

    private static PageImage Build(PageHarness pages, string path, string template) {
        PagePath key = PageHarness.Key(path);

        return pages.Images.Build(key, template, PageMetrics.ReasonSave, [], CancellationToken.None);
    }
}
