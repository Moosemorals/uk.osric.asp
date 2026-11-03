// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using System.Diagnostics;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>Proves the template → compilation step, without storage in the way.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// The two deadline tests measure a wall clock, so the fixture wants the process to itself.
[NonParallelizable]
public sealed class TemplateCompilerTests {

    [Test]
    public void CompileReturnsAnImageWithNoDiagnostics() {
        using PageHarness pages = new(new FakePageStore());

        PageCompilation compilation = Compile(pages, "/hello", "Hello.");

        Assert.That(compilation.Diagnostics, Is.Empty);
        Assert.That(compilation.Image, Is.Not.Null.And.Not.Empty);
        Assert.That(compilation.ContentHash, Is.EqualTo(ContentFingerprint.Of("Hello.")));
        Assert.That(compilation.RuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
    }

    [Test]
    public void AParseErrorDoesNotReachTheCompiler() {
        using PageHarness pages = new(new FakePageStore());

        PageCompilation compilation = Compile(pages, "/unterminated", "before<% if (true) {");

        Assert.That(compilation.Diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(1, 7),
                "TEMPLATE001: unterminated code block; the '<%' has no matching '%>'."),
        }));
        Assert.That(compilation.Image, Is.Null);
        Assert.That(pages.Compiler.CompileCount, Is.Zero, "a template that does not parse never reaches the compiler");
    }

    [Test]
    public void AWarningDoesNotStopThePage() {
        using PageHarness pages = new(new FakePageStore());

        PageCompilation compilation = Compile(pages, "/warned", "<% int unused = 1; %>");

        Assert.That(compilation.Image, Is.Not.Null.And.Not.Empty);
        Assert.That(compilation.Diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("CS0219", StringComparison.Ordinal)));
    }

    [Test]
    public void ACompilerFaultPropagates() {
        using PageHarness pages = new(new FakePageStore(), new FaultingCompiler());

        Assert.That(
            () => Compile(pages, "/fault", "Hello."),
            Throws.InvalidOperationException);
    }

    [Test]
    public void ACompileThatOutranItsBudgetIsReported() {
        PageBudget budget = new(new RenderOptions(), new CompileOptions { TimeoutSeconds = 1 });
        using PageHarness pages = new(new FakePageStore(), new StallingCompiler(), budget: budget);

        PageCompilation compilation = Compile(pages, "/stalled", "Hello.");

        Assert.That(compilation.Diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, null, "PAGE004: the page took longer than 1 s to compile."),
        }));
        Assert.That(compilation.Image, Is.Null);
    }

    [Test]
    public async Task ACompileThatWaitedPastItsBudgetForASlotIsReportedAsync() {
        PageBudget budget = new(new RenderOptions(), new CompileOptions { TimeoutSeconds = 1 });
        using PageHarness pages = new(new FakePageStore(), budget: budget);

        await budget.CompileSlots.WaitAsync();
        try {
            long start = Stopwatch.GetTimestamp();
            PageCompilation compilation = Compile(pages, "/queued", "Hello.");

            Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromSeconds(3)),
                "the wait for a slot is bounded by the compile budget");
            Assert.That(compilation.Diagnostics, Is.EqualTo(new PageDiagnostic[] {
                new(PageDiagnosticSeverity.Error, null,
                    "PAGE003: the page waited longer than 1 s for a compile slot."),
            }));
        } finally {
            budget.CompileSlots.Release();
        }

        Assert.That(pages.Compiler.CompileCount, Is.Zero);
    }

    private static PageCompilation Compile(PageHarness pages, string path, string content) {
        PagePath key = PageHarness.Key(path);

        return pages.Templates.Compile(key, content, PageMetrics.ReasonSave, CancellationToken.None);
    }
}
