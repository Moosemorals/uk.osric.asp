// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>The one save path, shared by the editor and by a page saving its own source.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageSaverTests {

    [Test]
    public async Task ACleanTemplateIsStoredWithItsImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        SavedRevision saved = await pages.Saver.SaveAsync(PageHarness.Key("/hello"), "Hello.", CancellationToken.None);

        Assert.That(saved.Diagnostics, Is.Empty);
        Assert.That(saved.Revision.Hash, Is.EqualTo(PageHarness.Stored(storage, "/hello").Hash));
        Assert.That(saved.Revision.Image?.Bytes, Is.Not.Null.And.Not.Empty);
        Assert.That(pages.Revisions.SaveCount, Is.EqualTo(1));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AWarningIsStoredWithItsImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/warned"), "<% int unused = 1; %>", CancellationToken.None);

        Assert.That(saved.Diagnostics, Has.Exactly(1).Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null && diagnostic.Severity == PageDiagnosticSeverity.Warning));
        Assert.That(PageHarness.Stored(storage, "/warned").Image?.Bytes, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task AnErrorIsStoredWithoutAnImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/broken"), "<% int value = ; %>", CancellationToken.None);

        Assert.That(saved.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null && diagnostic.Severity == PageDiagnosticSeverity.Error));
        PageTemplate stored = PageHarness.Stored(storage, "/broken");
        Assert.That(stored.Template, Is.EqualTo("<% int value = ; %>"));
        Assert.That(stored.Image?.Bytes, Is.Null);
        Assert.That(saved.Revision.Hash, Is.EqualTo(stored.Hash));
    }

    [Test]
    public async Task ACompilerFaultStoresTheSourceAndRethrowsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage, new FaultingCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.Saver.SaveAsync(PageHarness.Key("/fault"), "Hello.", CancellationToken.None));

        PageTemplate stored = PageHarness.Stored(storage, "/fault");
        Assert.That(stored.Template, Is.EqualTo("Hello."));
        Assert.That(stored.Image, Is.Null);
        Assert.That(stored.CreationRuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
    }

    [Test]
    public async Task TheResultNamesTheRevisionTheStoreKeptAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        SavedRevision saved = await pages.Saver.SaveAsync(PageHarness.Key("/hello"), "Hello.", CancellationToken.None);

        Assert.That(pages.Revisions.Last?.Hash, Is.EqualTo(saved.Revision.Hash));
        Assert.That(storage.LastWritePath, Is.EqualTo(PageHarness.Key("/hello")));
    }

    [Test]
    public async Task TheSaveIsRecordedAsASaveAsync() {
        FakePageStore storage = new();
        using MetricCapture capture = MetricCapture.Start();
        using PageHarness pages = new(storage);

        await pages.Saver.SaveAsync(PageHarness.Key("/hello"), "Hello.", CancellationToken.None);

        Assert.That(capture.Count("page.save.duration"), Is.EqualTo(1));
    }

    [Test]
    public async Task ARenderDeadlineStopsASaveThatNeverGotACompileSlotAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        using CancellationTokenSource expired = new();
        await expired.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pages.Saver.SaveAsync(PageHarness.Key("/hello"), "Hello.", CancellationToken.None, expired.Token));

        Assert.That(storage.WriteCount, Is.Zero, "nothing is compiled, so nothing is stored");
    }

    [Test]
    public async Task ASaveThatHasStartedIsNotCancelledByTheRenderDeadlineAsync() {
        FakePageStore storage = new();
        using CancellationTokenSource deadline = new();
        using PageHarness pages = new(storage, new DeadlineCompiler(new PageCompiler(), deadline.Cancel));

        // The render gives up while the page is compiling. The compile keeps its own bound and the
        // revision is stored, because losing the author's edit is the worse failure (DEC-191).
        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/hello"), "Hello.", CancellationToken.None, deadline.Token);

        Assert.That(deadline.IsCancellationRequested, Is.True);
        Assert.That(saved.Diagnostics, Is.Empty);
        Assert.That(PageHarness.Stored(storage, "/hello").Template, Is.EqualTo("Hello."));
        Assert.That(storage.WriteCount, Is.EqualTo(1));
    }

    /// <summary>Cancels the render's deadline once the compile has started.</summary>
    private sealed class DeadlineCompiler(IPageCompiler inner, Action onCompile) : IPageCompiler {

        public string RuntimeId => inner.RuntimeId;

        public CompileResult Compile(GeneratedSource source, CancellationToken cancellationToken = default) {
            onCompile();

            return inner.Compile(source, cancellationToken);
        }

        public LoadedPage Load(byte[] image, byte[]? symbols = null) {
            return inner.Load(image, symbols);
        }
    }
}
