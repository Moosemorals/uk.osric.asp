// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;
using CoreDiagnostic = uk.osric.asp.Core.Compilation.PageDiagnostic;
using CoreSeverity = uk.osric.asp.Core.Compilation.PageDiagnosticSeverity;
// The page's own diagnostic is the one these tests read; the compiler's is only the input.
using PageDiagnostic = uk.osric.asp.Sdk.PageDiagnostic;
using PageDiagnosticSeverity = uk.osric.asp.Sdk.PageDiagnosticSeverity;

[TestFixture]
public sealed class PageWritesScopeTests {

    private const string EditorPath = "/pages";

    private static readonly PageUser Owner = new("owner");

    [Test]
    public async Task ResolveNormalisesTheKeyAndReportsAShellFileAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWritesScope scope = Scope(pages);

        Assert.Multiple(() => {
            Assert.That(scope.Resolve("/Blog/").Key, Is.EqualTo("/blog"));
            Assert.That(scope.Resolve("/blog").IsReserved, Is.False);
            Assert.That(scope.Resolve("/login").IsReserved, Is.True);
            Assert.That(scope.Resolve("/page?x").Error, Does.Contain("not a page address"));
        });
    }

    [Test]
    public async Task AnAnonymousWriteIsRefusedBeforeAnythingIsStoredAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages, signedIn: false);

        await Assert.ThrowsAsync<UserFacingException>(async () => await scope.SaveAsync("/page", "text", "hash"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AHeadCannotWriteAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages, writable: false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/page", "text", "hash"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AShellFileIsRefusedAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/login", "text", "hash"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AStaleSaveWritesNothingAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);

        PageEditResult result = await scope.SaveAsync("/page", "second", "not-the-head");

        Assert.Multiple(() => {
            Assert.That(result.Outcome, Is.EqualTo(PageEditOutcome.Stale));
            Assert.That(PageHarness.Stored(storage, "/page").Template, Is.EqualTo("first"));
        });
    }

    [Test]
    public async Task ASaveThatStartedIsNotAbandonedWhenTheRenderEndsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);
        using CancellationTokenSource render = new();
        scope.AttachDeadline(render.Token);

        // The render ends while the save is in flight: a write that carried the render's deadline
        // would already be cancelled by the time the store saw it (`pages-a-page-may-save-itself`).
        pages.Revisions.OnSave = _ => render.Cancel();
        PageEditResult result = await scope.SaveAsync("/page", "second", first.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(result.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(pages.Revisions.LastCancellationToken.IsCancellationRequested, Is.False);
        });
    }

    [Test]
    public async Task ASaveStoresTheSourceAndReportsTheRevisionAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);

        PageEditResult result = await scope.SaveAsync("/page", "<% int unused = 1; %>", first.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(result.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(result.Hash, Is.Not.Null.And.Not.Empty);
            Assert.That(result.HasWarnings, Is.True);
            Assert.That(PageHarness.Stored(storage, "/page").Template, Is.EqualTo("<% int unused = 1; %>"));
        });
    }

    [Test]
    public async Task ANewPageIsSavedWithoutAnExpectationAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        PageEditResult result = await scope.SaveAsync("/new", "first", null);

        Assert.Multiple(() => {
            Assert.That(result.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(storage["/new"]?.Template, Is.EqualTo("first"));
        });
    }

    [Test]
    public async Task AnExpectationlessSaveCannotOverwriteAPageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);

        PageEditResult result = await scope.SaveAsync("/page", "second", null);

        Assert.Multiple(() => {
            Assert.That(result.Outcome, Is.EqualTo(PageEditOutcome.Stale), "the page exists now");
            Assert.That(storage["/page"]?.Template, Is.EqualTo("first"));
        });
    }

    [Test]
    public async Task OneRenderMakesOneActionAsync() {
        using PageHarness pages = new(new FakePageStore());
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);

        _ = await scope.SaveAsync("/page", "second", first.Revision.Hash);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/page", "third", "hash"));
    }

    [Test]
    public async Task DeleteTombstonesAndReportsWhatItFoundAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "text");
        PageWritesScope scope = Scope(pages);

        PageDeleteResult deleted = await scope.DeleteAsync("/page");
        PageWritesScope second = Scope(pages);
        PageDeleteResult missing = await second.DeleteAsync("/page");
        bool tombstoned = (await storage.GetHeadAsync(PageHarness.Key("/page")))?.IsDeleted ?? false;

        Assert.Multiple(() => {
            Assert.That(deleted, Is.EqualTo(new PageDeleteResult(true, true)));
            Assert.That(missing, Is.EqualTo(new PageDeleteResult(false, false)));
            Assert.That(tombstoned, Is.True);
        });
    }

    [Test]
    public async Task TheEditorKeyIsAnOrdinaryKeyAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        _ = await pages.Editor.SaveAsync(PageHarness.Key(EditorPath), "text");

        PageDeleteResult deleted = await Scope(pages, path: EditorPath).DeleteAsync(EditorPath);

        Assert.That(deleted, Is.EqualTo(new PageDeleteResult(true, true)), "the editor's own page is deletable now");
    }

    [Test]
    public async Task InstallingASeedTheReleaseMovedSavesItAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PagePath key = PageHarness.Key("/about");
        string seed = (TestOptions.SeedSource(key))!;
        storage["/about"] = new PageTemplate(key, "old") { SeedHash = TemplateFingerprint.Of("old") };
        string live = PageHarness.Stored(storage, "/about").Hash;
        PageWritesScope scope = Scope(pages);

        PageEditResult? result = await scope.InstallSeedAsync("/about", live);

        Assert.Multiple(() => {
            Assert.That(result?.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(PageHarness.Stored(storage, "/about").Template, Is.EqualTo(seed));
            Assert.That(storage["/about"]?.SeedHash, Is.EqualTo(TestOptions.Seeds.Find(key)?.Hash),
                "the install records the seed it installed");
        });
    }

    [Test]
    public async Task InstallingASeedThatMatchesWritesNothingAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PagePath key = PageHarness.Key("/about");
        string seed = (TestOptions.SeedSource(key))!;
        storage["/about"] = new PageTemplate(key, seed) { SeedHash = TestOptions.Seeds.Find(key)?.Hash };
        int writes = storage.WriteCount;
        PageWritesScope scope = Scope(pages);

        PageEditResult? result = await scope.InstallSeedAsync("/about", "hash");

        Assert.Multiple(() => {
            Assert.That(result?.Outcome, Is.EqualTo(PageEditOutcome.Unchanged));
            Assert.That(storage.WriteCount, Is.EqualTo(writes), "a matching seed is not written");
        });
    }

    [Test]
    public void InstallingASeedOverAnEditedPageIsRefused() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PagePath key = PageHarness.Key("/about");
        string seed = (TestOptions.SeedSource(key))!;
        storage["/about"] = new PageTemplate(key, seed) { SeedHash = TestOptions.Seeds.Find(key)?.Hash };
        storage["/about"] = new PageTemplate(key, "mine");

        Assert.That(
            async () => await Scope(pages).InstallSeedAsync("/about", "hash"),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("edited"));
    }

    [Test]
    public async Task AdoptingASeedThatMatchesRecordsItWithoutARevisionAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PagePath key = PageHarness.Key("/about");
        string seed = (TestOptions.SeedSource(key))!;
        string shipped = TestOptions.Seeds.Find(key)!.Hash;
        storage["/about"] = new PageTemplate(key, seed) { SeedHash = TemplateFingerprint.Of("other") };
        int writes = storage.WriteCount;

        PageEditResult? result = await Scope(pages).AdoptSeedAsync("/about");
        PageHeadSummary? head = await storage.GetHeadAsync(key);

        Assert.Multiple(() => {
            Assert.That(result?.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(storage.WriteCount, Is.EqualTo(writes), "adoption stores no revision");
            Assert.That(head?.SeedHash, Is.EqualTo(shipped));
        });
    }

    [Test]
    public void AdoptingAPageThatIsNotTheSeedIsRefused() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PagePath key = PageHarness.Key("/about");
        storage["/about"] = new PageTemplate(key, "mine") { SeedHash = TemplateFingerprint.Of("other") };

        Assert.That(
            async () => await Scope(pages).AdoptSeedAsync("/about"),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("conflict"));
    }

    [Test]
    public async Task InstallingSomethingThatIsNotASeedIsNotFoundAsync() {
        using PageHarness pages = new(new FakePageStore());

        Assert.That(await Scope(pages).InstallSeedAsync("/mine", null), Is.Null);
    }

    [Test]
    public async Task RestoringARevisionSavesItAsANewRevisionAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        SavedRevision second = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "second");
        PageWritesScope scope = Scope(pages);

        PageEditResult? result = await scope.RestoreRevisionAsync("/page", first.Revision.Hash, second.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(result?.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(PageHarness.Stored(storage, "/page").Template, Is.EqualTo("first"));
        });
    }

    [Test]
    public async Task RestoringASourceThePageAlreadyHoldsWritesNothingAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision only = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        int writes = storage.WriteCount;
        PageWritesScope scope = Scope(pages);

        PageEditResult? result = await scope.RestoreRevisionAsync("/page", only.Revision.Hash, only.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(result?.Outcome, Is.EqualTo(PageEditOutcome.Unchanged));
            Assert.That(storage.WriteCount, Is.EqualTo(writes));
        });
    }

    [Test]
    public async Task ARevisionOfAnotherPageIsNotFoundAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision other = await pages.Editor.SaveAsync(PageHarness.Key("/other"), "text");
        PageWritesScope scope = Scope(pages);

        Assert.That(await scope.RestoreRevisionAsync("/page", other.Revision.Hash, null), Is.Null);
    }

    [Test]
    public async Task AnAbandonedRenderCannotWriteAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWritesScope scope = Scope(pages);
        scope.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/page", "text", "hash"));
    }

    [Test]
    public async Task ADeadlineThatPassedCannotWriteAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWritesScope scope = Scope(pages);
        scope.AttachDeadline(new CancellationToken(canceled: true));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/page", "text", "hash"));
    }

    [Test]
    public async Task ASignedInVisitorSavesItsOwnSourceAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("<% ctx.Write(\"Hello.\"); %>");

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.HasWarnings, Is.False);
        Assert.That(result.Hash, Is.EqualTo(PageHarness.Stored(storage, "/self").Hash));
        Assert.That(PageHarness.Stored(storage, "/self").Template, Is.EqualTo("<% ctx.Write(\"Hello.\"); %>"));
        Assert.That(PageHarness.Stored(storage, "/self").Image?.Bytes, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task TheSaveIsBoundToThePagesOwnKeyAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        await Scope(pages, path: "/blog/self").SaveAsync("Hello.");

        Assert.That(PageHarness.Stored(storage, "/blog/self").Template, Is.EqualTo("Hello."));
        Assert.That(pages.Revisions.Last?.Path, Is.EqualTo(PageHarness.Key("/blog/self")));
    }

    [Test]
    public async Task AnAnonymousSaveIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());

        UserFacingException? refused = await Assert.ThrowsAsync<UserFacingException>(
            () => Scope(pages, signedIn: false).SaveAsync("Hello."));

        Assert.That(refused?.Message, Does.Contain("Sign in"));
    }

    [Test]
    public async Task AnAnonymousSaveStoresNothingAndCompilesNothingAsync() {
        using PageHarness pages = new(new FakePageStore());

        await Assert.ThrowsAsync<UserFacingException>(() => Scope(pages, signedIn: false).SaveAsync("Hello."));

        Assert.That(pages.Revisions.SaveCount, Is.Zero);
        Assert.That(pages.Compiler.CompileCount, Is.Zero);
    }

    [Test]
    public async Task ASaveOnHeadIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Scope(pages, writable: false).SaveAsync("Hello."));

        Assert.That(refused?.Message, Does.Contain("HEAD"));
        Assert.That(pages.Revisions.SaveCount, Is.Zero);
    }

    [Test]
    public async Task AnAbandonedRenderCannotSaveAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWritesScope writes = Scope(pages, path: "/self");

        writes.Abandon();

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writes.SaveAsync("Hello."));

        Assert.That(refused?.Message, Does.Contain("abandoned"));
        Assert.That(pages.Revisions.SaveCount, Is.Zero);
    }

    [Test]
    public async Task ASaveAfterTheDeadlineIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWritesScope writes = Scope(pages, path: "/self");
        using CancellationTokenSource deadline = new();
        writes.AttachDeadline(deadline.Token);
        await deadline.CancelAsync();

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writes.SaveAsync("Hello."));

        Assert.That(refused?.Message, Does.Contain("out of time"));
        Assert.That(pages.Revisions.SaveCount, Is.Zero);
    }

    [Test]
    public async Task ASecondSaveInOneRenderIsRefusedAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageWritesScope writes = Scope(pages, path: "/self");
        await writes.SaveAsync("First.");

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writes.SaveAsync("Second."));

        Assert.That(refused?.Message, Does.Contain("already edited"));
        Assert.That(pages.Revisions.SaveCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AWarningIsAnsweredAndThePageIsStillStoredAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("<% int unused = 1; %>");

        Assert.That(result.HasWarnings, Is.True);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(PageHarness.Stored(storage, "/self").Image?.Bytes, Is.Not.Null.And.Not.Empty, "a warning never stops a page");
    }

    [Test]
    public async Task AnErrorIsAnsweredWithItsPositionAndStoredWithoutAnImageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("<% int value = ; %>");

        PageDiagnostic error = result.Diagnostics.Single(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);
        Assert.That(result.HasErrors, Is.True);
        Assert.That(error.Line, Is.EqualTo(1));
        Assert.That(error.Column, Is.GreaterThan(0));
        Assert.That(PageHarness.Stored(storage, "/self").Template, Is.EqualTo("<% int value = ; %>"));
        Assert.That(PageHarness.Stored(storage, "/self").Image?.Bytes, Is.Null, "an error is stored and reported");
    }

    [Test]
    public async Task ADiagnosticCarriesItsCodeAndItsTextApartAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("before<% if (true) {");

        PageDiagnostic error = result.Diagnostics.Single();
        Assert.That(error.Code, Is.EqualTo("TEMPLATE001"));
        Assert.That(error.Message, Is.EqualTo("unterminated code block; the '<%' has no matching '%>'."));
        Assert.That(error.Line, Is.EqualTo(1));
        Assert.That(error.Column, Is.EqualTo(7));
    }

    [Test]
    public async Task ADiagnosticWithNoPositionHasNoLineOrColumnAsync() {
        using PageHarness pages = new(new FakePageStore(), new PositionlessCompiler());

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("<% ctx.Write(\"Hello.\"); %>");

        PageDiagnostic error = result.Diagnostics.Single();
        Assert.That(error.Line, Is.Null);
        Assert.That(error.Column, Is.Null);
        Assert.That(error.Code, Is.EqualTo("PAGE003"));
    }

    [Test]
    public async Task TheDiagnosticsAreOrderedForDisplayAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("<% int first = ; %>\n<p>fine</p>\n<% int second = ; %>");

        Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Line), Is.Ordered);
    }

    [Test]
    public async Task ACompilerFaultStoresTheSourceAndThrowsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage, new FaultingCompiler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Scope(pages, path: "/self").SaveAsync("<% ctx.Write(\"Hello.\"); %>"));

        Assert.That(PageHarness.Stored(storage, "/self").Template, Is.EqualTo("<% ctx.Write(\"Hello.\"); %>"));
        Assert.That(PageHarness.Stored(storage, "/self").Image, Is.Null);
    }

    [Test]
    public async Task TheSavedRevisionIsWhatTheNextRequestServesAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        await pages.StoreCompiledAsync("/self", "<h1>Before</h1>");

        PageWritesScope writes = Scope(pages, path: "/self");
        await writes.SaveAsync("<h1>After</h1>");

        // The running image is the one loaded before the save; the store holds the new revision.
        (RenderResult result, string output) = await pages.RenderAsync("/self");

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("After"));
    }

    [Test]
    public async Task APageSavesItselfDuringARenderAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        await pages.StoreCompiledAsync("/saving", """
            <% PageSaveResult saved = await ctx.Capabilities.GetRequired<IPageWrites>().SaveAsync("<h1>Saved</h1>"); %>
            <h1>Saved</h1><p id="hash"><%= saved.Hash %></p>
            """);

        (RenderResult result, string output) = await pages.RenderAsync("/saving", user: Owner);

        Assert.That(result.Status, Is.EqualTo(RenderStatus.Rendered));
        Assert.That(output, Does.Contain("<h1>Saved</h1>"));
        Assert.That(PageHarness.Stored(storage, "/saving").Template, Is.EqualTo("<h1>Saved</h1>"));
        Assert.That(pages.Compiler.CompileCount, Is.EqualTo(1), "the saved template has no code, so it is stored without compiling");
    }

    [Test]
    public async Task APageSavesAnotherPageAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/other"), "before");
        string head = PageHarness.Stored(storage, "/other").Hash;

        PageEditResult result = await Scope(pages, path: "/writer").SaveAsync("/other", "after", head);

        Assert.Multiple(() => {
            Assert.That(result.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(PageHarness.Stored(storage, "/other").Template, Is.EqualTo("after"));
            Assert.That(pages.Revisions.Last?.Path, Is.EqualTo(PageHarness.Key("/other")),
                "the page that asked is not the page that changed");
        });
    }

    [Test]
    public async Task TheBoundSaveAndTheNamedSaveShareOneActionAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope writes = Scope(pages, path: "/self");

        await writes.SaveAsync("first");

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writes.SaveAsync("/other", "second", null));

        Assert.Multiple(() => {
            Assert.That(refused?.Message, Does.Contain("already edited"));
            Assert.That(pages.Revisions.SaveCount, Is.EqualTo(1), "one render makes one write, whichever save it uses");
        });
    }

    private static PageWritesScope Scope(
        PageHarness pages,
        bool writable = true,
        PageUser? user = null,
        bool signedIn = true,
        string path = EditorPath) => new(
        PageHarness.Key(path),
        pages.Editor,
        signedIn ? user ?? Owner : null,
        writable,
        new ReservedPaths(ReservedPaths.Default),
        TestOptions.Seeds);

    /// <summary>Reports one diagnostic the way a compile timeout does: with no position at all.</summary>
    private sealed class PositionlessCompiler : IPageCompiler {

        public string RuntimeId => "test:positionless";

        public CompileResult Compile(GeneratedSource source, IReadOnlyList<LibrarySource> libraries, CancellationToken cancellationToken = default) =>
            new(null, null, [
                new CoreDiagnostic(CoreSeverity.Error, null,
                    "PAGE003: the page waited longer than 10 s for a compile slot."),
            ]);

        public CompileResult CompileWorker(GeneratedSource source, IReadOnlyList<LibrarySource> libraries, CancellationToken cancellationToken = default) =>
            Compile(source, libraries, cancellationToken);

        public LoadedPage Load(byte[] image, byte[]? symbols = null) =>
            throw new NotSupportedException("this compiler never produces an image");

        public LoadedWorker LoadWorker(byte[] image, byte[]? symbols = null) =>
            throw new NotSupportedException("this compiler never produces an image");
    }

}
