// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>Proves what a save stores, and what it keeps when the compile fails.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageEditorTests {

    [Test]
    public async Task SaveBuildsOnceAndStoresTheTemplateWithTheImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.Editor.SaveAsync(PageHarness.Key("/hello"), "Hello.");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));

        PageTemplate saved = PageHarness.Stored(storage, "/hello");
        Assert.That(saved.Template, Is.EqualTo("Hello."));
        Assert.That(saved.Image?.Bytes, Is.Not.Null.And.Not.Empty);
        Assert.That(saved.Image?.TemplateHash, Is.EqualTo(TemplateFingerprint.Of("Hello.")));
        Assert.That(saved.Image?.RuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
        Assert.That(saved.CreationRuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
        Assert.That(saved.RevisionId, Is.Not.Empty);
        Assert.That(saved.Hash, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(saved.Image?.Diagnostics, Is.Empty);
    }

    [Test]
    public async Task SaveKeepsTheTemplateAndStoresDiagnosticsWhenThePageDoesNotCompileAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.Editor.SaveAsync(PageHarness.Key("/broken"), "<% int value = ; %>");

        Assert.That(diagnostics, Is.Not.Empty);
        // NUnit types the Matches predicate's item as nullable, so check it before reading it.
        Assert.That(diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS", StringComparison.Ordinal)
                && diagnostic.Severity == PageDiagnosticSeverity.Error));

        PageTemplate saved = PageHarness.Stored(storage, "/broken");
        Assert.That(saved.Template, Is.EqualTo("<% int value = ; %>"));
        Assert.That(saved.Image?.Bytes, Is.Null);
        Assert.That(saved.Image?.Diagnostics, Is.EqualTo(diagnostics).AsCollection);
    }

    [Test]
    public async Task SaveStoresAParseErrorWithoutCallingTheCompilerAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.Editor.SaveAsync(PageHarness.Key("/unterminated"), "before<% if (true) {");

        Assert.That(diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(1, 7),
                "TEMPLATE001: unterminated code block; the '<%' has no matching '%>'."),
        }));
        Assert.That(pages.Compiler.CompileCount, Is.Zero, "a template that does not parse never reaches the compiler");

        PageTemplate saved = PageHarness.Stored(storage, "/unterminated");
        Assert.That(saved.Template, Is.EqualTo("before<% if (true) {"));
        Assert.That(saved.Image?.Bytes, Is.Null);
        Assert.That(saved.Image?.Diagnostics, Is.EqualTo(diagnostics).AsCollection);
    }

    [Test]
    public async Task SaveReportsAWarningWithoutFailingThePageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.Editor.SaveAsync(PageHarness.Key("/warned"), "<% int unused = 1; %>");

        Assert.That(diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("CS0219", StringComparison.Ordinal)));

        PageTemplate saved = PageHarness.Stored(storage, "/warned");
        Assert.That(saved.Image?.Bytes, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task ConditionalSaveCompilesAndReturnsTheNewRevisionHashAsync() {
        FakePageStore storage = new();
        PageTemplate original = new(PageHarness.Key("/conditional"), "first");
        storage["/conditional"] = original;
        using PageHarness pages = new(storage);

        ConditionalPageSaveResult result = await pages.Editor.SaveIfRevisionAsync(
            PageHarness.Key("/conditional"), "second", original.Hash);

        Assert.That(result.Saved, Is.True);
        Assert.That(result.RevisionHash, Is.EqualTo(PageHarness.Stored(storage, "/conditional").Hash));
        Assert.That(PageHarness.Stored(storage, "/conditional").Template, Is.EqualTo("second"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public async Task ConditionalSaveReturnsConflictWithoutWritingForAStaleHashAsync() {
        FakePageStore storage = new();
        PageTemplate original = new(PageHarness.Key("/conditional"), "first");
        storage["/conditional"] = original;
        using PageHarness pages = new(storage);

        ConditionalPageSaveResult result = await pages.Editor.SaveIfRevisionAsync(
            PageHarness.Key("/conditional"), "stale replacement", "old-hash");

        Assert.That(result.Saved, Is.False);
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(PageHarness.Stored(storage, "/conditional").Template, Is.EqualTo("first"));
    }

    [Test]
    public async Task SavePreservesTheTemplateWhenTheCompilerFaultsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage, new FaultingCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.Editor.SaveAsync(PageHarness.Key("/fault"), "Hello."));

        PageTemplate saved = PageHarness.Stored(storage, "/fault");
        Assert.That(saved.Template, Is.EqualTo("Hello."));
        Assert.That(saved.Image, Is.Null);
        Assert.That(saved.CreationRuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
    }

    [Test]
    public async Task GetReturnsTheStoredPageWithoutCompilingAsync() {
        FakePageStore storage = new();
        storage["/hello"] = new PageTemplate(PageHarness.Key("/hello"), "Hello.");
        using PageHarness pages = new(storage);

        PageTemplate? page = await pages.Editor.GetAsync(PageHarness.Key("/hello"));

        Assert.That(page?.Template, Is.EqualTo("Hello."));
        Assert.That(pages.Compiler.CompileCount, Is.Zero);
    }

    [Test]
    public async Task GetReturnsNullForAMissingPageAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageTemplate? page = await pages.Editor.GetAsync(PageHarness.Key("/missing"));

        Assert.That(page, Is.Null);
    }
}
