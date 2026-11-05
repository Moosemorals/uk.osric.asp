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
    public async Task SaveCompilesOnceAndStoresTheSourceWithTheImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.Editor.SaveAsync(PageHarness.Key("/hello"), "Hello.");

        Assert.That(diagnostics, Is.Empty);
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));

        StoredPage saved = PageHarness.Stored(storage, "/hello");
        Assert.That(saved.Content, Is.EqualTo("Hello."));
        Assert.That(saved.Compilation?.Image, Is.Not.Null.And.Not.Empty);
        Assert.That(saved.Compilation?.ContentHash, Is.EqualTo(ContentFingerprint.Of("Hello.")));
        Assert.That(saved.Compilation?.RuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
        Assert.That(saved.CreationRuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
        Assert.That(saved.RevisionId, Is.Not.Empty);
        Assert.That(saved.Hash, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(saved.Compilation?.Diagnostics, Is.Empty);
    }

    [Test]
    public async Task SaveKeepsTheSourceAndStoresDiagnosticsWhenThePageDoesNotCompileAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        IReadOnlyList<PageDiagnostic> diagnostics = await pages.Editor.SaveAsync(PageHarness.Key("/broken"), "<% int value = ; %>");

        Assert.That(diagnostics, Is.Not.Empty);
        // NUnit types the Matches predicate's item as nullable, so check it before reading it.
        Assert.That(diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.Contains("CS", StringComparison.Ordinal)
                && diagnostic.Severity == PageDiagnosticSeverity.Error));

        StoredPage saved = PageHarness.Stored(storage, "/broken");
        Assert.That(saved.Content, Is.EqualTo("<% int value = ; %>"));
        Assert.That(saved.Compilation?.Image, Is.Null);
        Assert.That(saved.Compilation?.Diagnostics, Is.EqualTo(diagnostics).AsCollection);
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

        StoredPage saved = PageHarness.Stored(storage, "/unterminated");
        Assert.That(saved.Content, Is.EqualTo("before<% if (true) {"));
        Assert.That(saved.Compilation?.Image, Is.Null);
        Assert.That(saved.Compilation?.Diagnostics, Is.EqualTo(diagnostics).AsCollection);
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

        StoredPage saved = PageHarness.Stored(storage, "/warned");
        Assert.That(saved.Compilation?.Image, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task ConditionalSaveCompilesAndReturnsTheNewRevisionHashAsync() {
        FakePageStore storage = new();
        StoredPage original = new(PageHarness.Key("/conditional"), "first");
        storage["/conditional"] = original;
        using PageHarness pages = new(storage);

        ConditionalPageSaveResult result = await pages.Editor.SaveIfRevisionAsync(
            PageHarness.Key("/conditional"), "second", original.Hash);

        Assert.That(result.Saved, Is.True);
        Assert.That(result.RevisionHash, Is.EqualTo(PageHarness.Stored(storage, "/conditional").Hash));
        Assert.That(PageHarness.Stored(storage, "/conditional").Content, Is.EqualTo("second"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public async Task ConditionalSaveReturnsConflictWithoutWritingForAStaleHashAsync() {
        FakePageStore storage = new();
        StoredPage original = new(PageHarness.Key("/conditional"), "first");
        storage["/conditional"] = original;
        using PageHarness pages = new(storage);

        ConditionalPageSaveResult result = await pages.Editor.SaveIfRevisionAsync(
            PageHarness.Key("/conditional"), "stale replacement", "old-hash");

        Assert.That(result.Saved, Is.False);
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(PageHarness.Stored(storage, "/conditional").Content, Is.EqualTo("first"));
    }

    [Test]
    public async Task SavePreservesTheSourceWhenTheCompilerFaultsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage, new FaultingCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.Editor.SaveAsync(PageHarness.Key("/fault"), "Hello."));

        StoredPage saved = PageHarness.Stored(storage, "/fault");
        Assert.That(saved.Content, Is.EqualTo("Hello."));
        Assert.That(saved.Compilation, Is.Null);
        Assert.That(saved.CreationRuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
    }

    [Test]
    public async Task GetReturnsTheStoredPageWithoutCompilingAsync() {
        FakePageStore storage = new();
        storage["/hello"] = new StoredPage(PageHarness.Key("/hello"), "Hello.");
        using PageHarness pages = new(storage);

        StoredPage? page = await pages.Editor.GetAsync(PageHarness.Key("/hello"));

        Assert.That(page?.Content, Is.EqualTo("Hello."));
        Assert.That(pages.Compiler.CompileCount, Is.Zero);
    }

    [Test]
    public async Task GetReturnsNullForAMissingPageAsync() {
        using PageHarness pages = new(new FakePageStore());

        StoredPage? page = await pages.Editor.GetAsync(PageHarness.Key("/missing"));

        Assert.That(page, Is.Null);
    }
}
