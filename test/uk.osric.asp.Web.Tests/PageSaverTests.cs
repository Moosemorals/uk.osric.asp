// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>The one save path, shared by the editor and by a page saving its own source.</summary>
/// <remarks>
/// Not parallel: one test counts measurements on the pages meter, which every other fixture's save
/// also writes to, so a save running beside it would be counted as this test's.
/// </remarks>
[TestFixture]
[NonParallelizable]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageSaverTests {

    private const string WorkerTemplate = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Echo : IWorker {
                public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
            }
        }
        %>
        """;

    [Test]
    public async Task ACleanTemplateIsStoredWithItsImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/hello"), "<% ctx.Response.Write(\"Hello.\"); %>", CancellationToken.None);

        Assert.That(saved.Diagnostics, Is.Empty);
        Assert.That(saved.Revision.Hash, Is.EqualTo(PageHarness.Stored(storage, "/hello").Hash));
        Assert.That(saved.Revision.Image?.Bytes, Is.Not.Null.And.Not.Empty);
        Assert.That(pages.Revisions.SaveCount, Is.EqualTo(1));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AConditionalSaveStoresOnlyWhileTheHeadMatchesAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Saver.SaveAsync(PageHarness.Key("/blog"), "first", CancellationToken.None);

        ConditionalPageSaveResult stale = await pages.Saver.SaveIfRevisionAsync(
            PageHarness.Key("/blog"), "second", "not-the-head", CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(stale.Saved, Is.False);
            Assert.That(pages.Revisions.SaveCount, Is.EqualTo(1), "a stale save writes nothing");
            Assert.That(PageHarness.Stored(storage, "/blog").Template, Is.EqualTo("first"));
        });

        ConditionalPageSaveResult saved = await pages.Saver.SaveIfRevisionAsync(
            PageHarness.Key("/blog"), "second", first.Revision.Hash, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(saved.Saved, Is.True);
            Assert.That(saved.RevisionHash, Is.Not.Null.And.Not.Empty);
            Assert.That(PageHarness.Stored(storage, "/blog").Template, Is.EqualTo("second"));
            Assert.That(pages.Revisions.SaveCount, Is.EqualTo(2));
        });
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
    public async Task ASourceOnlySaveTakesNoCompileSlotAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        await pages.Budget.CompileSlots.WaitAsync();
        try {
            SavedRevision saved = await pages.Saver.SaveAsync(
                PageHarness.Key("/site.css"), "body { color: red; }", CancellationToken.None);

            Assert.That(saved.Revision.Kind, Is.EqualTo(PageKind.Text));
            Assert.That(saved.Revision.Image, Is.Not.Null);
            Assert.That(saved.Revision.Image?.Bytes, Is.Null);
            Assert.That(PageHarness.Stored(storage, "/site.css").Template, Is.EqualTo("body { color: red; }"));
            Assert.That(pages.Compiler.CompileCount, Is.Zero);
        } finally {
            _ = pages.Budget.CompileSlots.Release();
        }
    }

    [Test]
    public async Task AWorkerIsStoredWithItsImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/_work/echo"), WorkerTemplate, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(saved.Diagnostics, Is.Empty);
            Assert.That(saved.Revision.Kind, Is.EqualTo(PageKind.Worker));
            Assert.That(saved.Revision.Image?.Bytes, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task TwoWorkerEntryPointsAreStoredWithTheirDiagnosticAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/_work/two"), """
            <%!
            using System.Threading.Tasks;

            namespace Site.Work {
                public sealed class One : IWorker {
                    public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
                }

                public sealed class Two : IWorker {
                    public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
                }
            }
            %>
            """, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(saved.Revision.Kind, Is.EqualTo(PageKind.Worker));
            Assert.That(saved.Revision.Image?.Bytes, Is.Null);
            Assert.That(saved.Diagnostics, Has.Some.Matches<PageDiagnostic>(
                diagnostic => diagnostic is not null
                    && diagnostic.Message.StartsWith("PAGE006:", StringComparison.Ordinal)));
        });
    }

    [Test]
    public async Task APageThatNamesAWorkerGetsAMissingNamespaceAsync() {
        FakePageReadStore read = new();
        read.Save("/_work/echo", WorkerTemplate, templateKind: PageKind.Worker, runtimeId: "test:runtime", hasImage: true);
        FakePageStore storage = new();
        using PageHarness pages = new(storage, readStore: read);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/uses-worker"),
            "<%! using Site.Work; %><p>Hello.</p>",
            CancellationToken.None);

        Assert.That(saved.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Message.StartsWith("CS0246:", StringComparison.Ordinal)),
            "a worker is a program, not a library a page may compile against");
    }

    [Test]
    public async Task AWorkerRecordsTheLibraryItWasCompiledAgainstAsync() {
        const string Document = "<%! namespace Site.Document { public static class Chrome { public static string Title() => \"Site\"; } } %>";
        const string Source = """
            <%!
            using System.Threading.Tasks;
            using Site.Document;

            namespace Site.Work {
                public sealed class Echo : IWorker {
                    public string Title { get; private set; } = string.Empty;

                    public Task RunAsync(IWorkerContext context) {
                        Title = Chrome.Title();

                        return Task.CompletedTask;
                    }
                }
            }
            %>
            """;
        FakePageReadStore read = new();
        string hash = read.Save("/_lib/document", Document, templateKind: PageKind.Library, runtimeId: "test:runtime");
        FakePageStore storage = new();
        using PageHarness pages = new(storage, readStore: read);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/_work/echo"), Source, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(saved.Diagnostics, Is.Empty);
            Assert.That(saved.Revision.Image!.IsCurrent(
                TemplateFingerprint.Of(Source), pages.Compiler.RuntimeId, $"/_lib/document@{hash}"), Is.True);
            Assert.That(saved.Revision.Image!.IsCurrent(
                TemplateFingerprint.Of(Source), pages.Compiler.RuntimeId, "/_lib/document@moved"), Is.False,
                "a worker whose library moved is compiled again before it is run");
        });
    }

    [Test]
    public async Task ALibraryThatNamesAWorkerGetsAMissingNamespaceAsync() {
        FakePageReadStore read = new();
        read.Save("/_work/echo", WorkerTemplate, templateKind: PageKind.Worker, runtimeId: "test:runtime", hasImage: true);
        FakePageStore storage = new();
        using PageHarness pages = new(storage, readStore: read);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/_lib/uses-worker"),
            "<%! using Site.Work; namespace Site.Helpers { public static class Help { } } %>",
            CancellationToken.None);

        Assert.That(saved.Diagnostics, Has.Some.Matches<PageDiagnostic>(
            diagnostic => diagnostic is not null
                && diagnostic.Severity == PageDiagnosticSeverity.Error
                && diagnostic.Message.StartsWith("CS0234:", StringComparison.Ordinal)),
            "a worker's namespace is not offered as a library: 'Work' does not exist in 'Site'");
    }

    [Test]
    public async Task ACompilerFaultStoresTheSourceAndRethrowsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage, new FaultingCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pages.Saver.SaveAsync(
                PageHarness.Key("/fault"), "<% ctx.Response.Write(\"Hello.\"); %>", CancellationToken.None));

        PageTemplate stored = PageHarness.Stored(storage, "/fault");
        Assert.That(stored.Template, Is.EqualTo("<% ctx.Response.Write(\"Hello.\"); %>"));
        Assert.That(stored.Image, Is.Null);
        Assert.That(stored.Kind, Is.Null, "a compiler fault never judged the source");
        Assert.That(stored.CreationRuntimeId, Is.EqualTo(pages.Compiler.RuntimeId));
    }

    [Test]
    public async Task TheResultNamesTheRevisionTheStoreKeptAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/hello"), "<% ctx.Response.Write(\"Hello.\"); %>", CancellationToken.None);

        Assert.That(pages.Revisions.Last?.Hash, Is.EqualTo(saved.Revision.Hash));
        Assert.That(storage.LastWritePath, Is.EqualTo(PageHarness.Key("/hello")));
    }

    [Test]
    [NonParallelizable]
    public async Task TheSaveIsRecordedAsASaveAsync() {
        // A meter listener hears every save in the process, so another fixture saving a page in this
        // window would be counted here. The counting cases opt out of the fixture-level parallelism.
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
            () => pages.Saver.SaveAsync(
                PageHarness.Key("/hello"), "<% ctx.Response.Write(\"Hello.\"); %>", CancellationToken.None, expired.Token));

        Assert.That(storage.WriteCount, Is.Zero, "nothing is compiled, so nothing is stored");
    }

    [Test]
    public async Task ASaveThatHasStartedIsNotCancelledByTheRenderDeadlineAsync() {
        FakePageStore storage = new();
        using CancellationTokenSource deadline = new();
        using PageHarness pages = new(storage, new DeadlineCompiler(new PageCompiler(), deadline.Cancel));

        // The render gives up while the page is compiling. The compile keeps its own bound and the
        // revision is stored, because losing the author's edit is the worse failure (`pages-a-page-may-save-itself`).
        SavedRevision saved = await pages.Saver.SaveAsync(
            PageHarness.Key("/hello"), "<% ctx.Response.Write(\"Hello.\"); %>", CancellationToken.None, deadline.Token);

        Assert.That(deadline.IsCancellationRequested, Is.True);
        Assert.That(saved.Diagnostics, Is.Empty);
        Assert.That(PageHarness.Stored(storage, "/hello").Template, Is.EqualTo("<% ctx.Response.Write(\"Hello.\"); %>"));
        Assert.That(storage.WriteCount, Is.EqualTo(1));
    }

    /// <summary>Cancels the render's deadline once the compile has started.</summary>
    private sealed class DeadlineCompiler(IPageCompiler inner, Action onCompile) : IPageCompiler {

        public string RuntimeId => inner.RuntimeId;

        public CompileResult Compile(GeneratedSource source, IReadOnlyList<LibrarySource> libraries, CancellationToken cancellationToken = default) {
            onCompile();

            return inner.Compile(source, libraries, cancellationToken);
        }

        public CompileResult CompileWorker(GeneratedSource source, IReadOnlyList<LibrarySource> libraries, CancellationToken cancellationToken = default) {
            onCompile();

            return inner.CompileWorker(source, libraries, cancellationToken);
        }

        public LoadedPage Load(byte[] image, byte[]? symbols = null) {
            return inner.Load(image, symbols);
        }

        public LoadedWorker LoadWorker(byte[] image, byte[]? symbols = null) {
            return inner.LoadWorker(image, symbols);
        }
    }
}
