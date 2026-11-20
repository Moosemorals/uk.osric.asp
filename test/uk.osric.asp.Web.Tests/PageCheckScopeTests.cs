// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using CorePageKind = uk.osric.asp.Core.Pages.PageKind;
using SdkDiagnostic = uk.osric.asp.Sdk.PageDiagnostic;
using SdkKind = uk.osric.asp.Sdk.PageKind;
using SdkSeverity = uk.osric.asp.Sdk.PageDiagnosticSeverity;

/// <summary>The capability that answers "would this source check" without storing anything.</summary>
[TestFixture]
public sealed class PageCheckScopeTests {

    [Test]
    public async Task AnUnsavedTemplateAnswersItsDiagnosticsAndStoresNothingAsync() {
        using PageHarness pages = new(new FakePageStore());
        await pages.StoreCompiledAsync("/held", "<p>held</p>");
        PageTemplate before = PageHarness.Stored(pages.Storage, "/held");
        int saves = pages.Revisions.SaveCount;

        PageCheckResult result = await CheckAsync(pages, "<% int broken = ; %>");

        Assert.Multiple(() => {
            Assert.That(result.Valid, Is.False);
            Assert.That(result.Diagnostics, Has.Some.Matches<SdkDiagnostic>(
                diagnostic => diagnostic.Severity == SdkSeverity.Error));
            Assert.That(pages.Revisions.SaveCount, Is.EqualTo(saves), "a check stores no revision");
            Assert.That(PageHarness.Stored(pages.Storage, "/held").Hash, Is.EqualTo(before.Hash),
                "a check leaves the head where it was");
        });
    }

    [Test]
    public async Task ABrokenTemplateAnswersAnErrorInTemplateSpaceAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageCheckResult result = await CheckAsync(pages, "<% int broken = ; %>");

        SdkDiagnostic error = result.Diagnostics.First(diagnostic => diagnostic.Severity == SdkSeverity.Error);
        Assert.Multiple(() => {
            Assert.That(result.Kind, Is.EqualTo(SdkKind.Page));
            Assert.That(error.Line, Is.Not.Null, "the author is shown where the problem is");
            Assert.That(error.Source, Is.Null, "the error is in the page being checked");
        });
    }

    [Test]
    public async Task ATemplateThatDoesNotParseAnswersTheTokenizersDiagnosticAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageCheckResult result = await CheckAsync(pages, "before<% if (true) {");

        Assert.Multiple(() => {
            Assert.That(result.Valid, Is.False);
            Assert.That(result.Diagnostics, Has.Some.Matches<SdkDiagnostic>(
                diagnostic => diagnostic.Code == "TEMPLATE001"));
            Assert.That(pages.Compiler.CompileCount, Is.Zero, "a template that does not parse never reaches the compiler");
        });
    }

    [Test]
    public async Task AWarningDoesNotStopTheAnswerBeingValidAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageCheckResult result = await CheckAsync(pages, "<% ctx.Response.Write(\"one\"); %>\n100%> done");

        Assert.Multiple(() => {
            Assert.That(result.Valid, Is.True, "a warning still serves");
            Assert.That(result.Diagnostics, Has.Some.Matches<SdkDiagnostic>(
                diagnostic => diagnostic.Code == "TEMPLATE003" && diagnostic.Severity == SdkSeverity.Warning));
        });
    }

    [Test]
    public async Task ASourceWithNoCodeAnswersTextWithoutCompilingAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageCheckResult result = await CheckAsync(pages, "<h1>Just text</h1>");

        Assert.Multiple(() => {
            Assert.That(result.Kind, Is.EqualTo(SdkKind.Text));
            Assert.That(result.Valid, Is.True);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(pages.Compiler.CompileCount, Is.Zero, "there was nothing to compile");
        });
    }

    [Test]
    public async Task ADeclarationOnlySourceAnswersLibraryAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageCheckResult result = await CheckAsync(
            pages, "<%! namespace Site { public static class Document { } } %>");

        Assert.Multiple(() => {
            Assert.That(result.Kind, Is.EqualTo(SdkKind.Library));
            Assert.That(result.Valid, Is.True);
        });
    }

    [Test]
    public async Task ADeclarationOnlySourceWithAnEntryPointAnswersWorkerAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageCheckResult result = await CheckAsync(pages, """
            <%! namespace Site.Work {
                    public sealed class Echo : IWorker {
                        public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
                    }
                } %>
            """);

        Assert.That(result.Kind, Is.EqualTo(SdkKind.Worker));
    }

    [Test]
    public async Task ALibrarysErrorNamesTheLibraryAsync() {
        FakePageReadStore libraries = new();
        libraries.Save("/_lib/broken",
            "<%! namespace Site { public static class Broken { public static int Value = \"no\"; } } %>",
            templateKind: CorePageKind.Library);
        using PageHarness pages = new(new FakePageStore(), readStore: libraries);

        PageCheckResult result = await CheckAsync(pages, "<%! using Site; %><%= Broken.Value %>");

        Assert.That(result.Diagnostics, Has.Some.Matches<SdkDiagnostic>(
            diagnostic => diagnostic.Source == "/_lib/broken" && diagnostic.Severity == SdkSeverity.Error));
    }

    [Test]
    public async Task ASourceIsCheckedTheSameWhetherOrNotItIsSavedAsync() {
        using PageHarness pages = new(new FakePageStore());
        const string Source = "<% int broken = ; %>";

        PageCheckResult before = await CheckAsync(pages, Source);
        await pages.StoreCompiledAsync("/same", Source);
        PageCheckResult after = await CheckAsync(pages, Source);

        Assert.Multiple(() => {
            Assert.That(after.Kind, Is.EqualTo(before.Kind));
            Assert.That(after.Valid, Is.EqualTo(before.Valid));
            Assert.That(after.Diagnostics, Is.EqualTo(before.Diagnostics), "the check has no key to miss");
        });
    }

    [Test]
    public async Task AnUnknownLanguageIsRefusedByNameAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageCheckScope scope = pages.Checks.For(PageHarness.Key("/checker"));

        ArgumentException? refused = await Assert.ThrowsAsync<ArgumentException>(
            () => scope.CheckAsync("css", "body { color: red; }"));

        Assert.Multiple(() => {
            Assert.That(refused?.Message, Does.Contain("css"));
            Assert.That(refused?.Message, Does.Contain("template"), "the refusal names what a check answers for");
        });
    }

    [Test]
    public async Task AnAbandonedCheckAnswersRatherThanCompilingAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageCheckScope scope = pages.Checks.For(PageHarness.Key("/checker"));
        scope.Abandon();

        PageCheckResult result = await scope.CheckAsync("template", "<% ctx.Response.Write(\"hi\"); %>");

        Assert.Multiple(() => {
            Assert.That(result.Valid, Is.False);
            Assert.That(result.Diagnostics, Has.Exactly(1).Matches<SdkDiagnostic>(
                diagnostic => diagnostic.Code == "PAGE010"));
            Assert.That(pages.Compiler.CompileCount, Is.Zero);
        });
    }

    [Test]
    public async Task TheDeadlineEndsTheWaitForACompileSlotAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageCheckScope scope = pages.Checks.For(PageHarness.Key("/checker"));
        using CancellationTokenSource deadline = new();
        deadline.CancelAfter(TimeSpan.FromMilliseconds(50));
        scope.AttachDeadline(deadline.Token);
        await pages.Budget.CompileSlots.WaitAsync();
        try {
            PageCheckResult result = await scope.CheckAsync("template", "<% ctx.Response.Write(\"hi\"); %>");

            Assert.That(result.Diagnostics, Has.Exactly(1).Matches<SdkDiagnostic>(
                diagnostic => diagnostic.Code == "PAGE010"
                    && diagnostic.Message.Contains("waited for a compile slot", StringComparison.Ordinal)));
        } finally {
            pages.Budget.CompileSlots.Release();
        }
    }

    [Test]
    public async Task AFullCompileQueueAnswersRatherThanWaitingForEverAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageBudget budget = new(
            TestOptions.FixtureRender,
            TestOptions.FixtureCompile with { TimeoutSeconds = PageBudget.MinimumTimeoutSeconds },
            TestOptions.Stream);
        PageCheckScope scope = new(
            PageHarness.Key("/checker"), pages.Compiler, pages.Libraries, budget, pages.Metrics,
            NullLogger<PageCheckScope>.Instance);
        await budget.CompileSlots.WaitAsync();
        try {
            PageCheckResult result = await scope.CheckAsync("template", "<% ctx.Response.Write(\"hi\"); %>");

            Assert.That(result.Diagnostics, Has.Exactly(1).Matches<SdkDiagnostic>(
                diagnostic => diagnostic.Code == "PAGE010" && diagnostic.Message.Contains("waited longer than", StringComparison.Ordinal)));
        } finally {
            budget.CompileSlots.Release();
        }
    }

    [Test]
    public async Task ACheckGivesItsCompileSlotBackAsync() {
        using PageHarness pages = new(new FakePageStore());
        int before = pages.Budget.CompileSlots.CurrentCount;

        await CheckAsync(pages, "<% ctx.Response.Write(\"hi\"); %>");

        Assert.That(pages.Budget.CompileSlots.CurrentCount, Is.EqualTo(before));
    }

    [Test]
    public async Task ACheckRecordsTheCompileItSpentAsync() {
        using MetricCapture metrics = MetricCapture.Start();
        using PageHarness pages = new(new FakePageStore());

        _ = await CheckAsync(pages, "<% ctx.Response.Write(\"hi\"); %>");

        Assert.That(metrics.For("page.compile.duration", "/checker"), Has.Some.Matches<MetricCapture.Measurement>(
            measurement => measurement is not null && measurement.HasTag("reason", PageMetrics.ReasonCheck)));
    }

    private static Task<PageCheckResult> CheckAsync(PageHarness pages, string source) =>
        pages.Checks.For(PageHarness.Key("/checker")).CheckAsync("template", source);
}
