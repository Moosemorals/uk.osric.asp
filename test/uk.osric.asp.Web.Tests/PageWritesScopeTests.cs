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
using PageKind = uk.osric.asp.Core.Pages.PageKind;

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
    public async Task ARenderMayCommitSeveralTransactionsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        PageEditResult first = await scope.SaveAsync("/page", "first", null);
        PageEditResult second = await scope.SaveAsync("/other", "second", null);

        Assert.Multiple(() => {
            Assert.That(first.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(second.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(storage["/page"]?.Template, Is.EqualTo("first"));
            Assert.That(storage["/other"]?.Template, Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task ATransactionThatIsDisposedWithoutCommittingWritesNothingAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using (scope.BeginTransaction()) {
            _ = await scope.SaveAsync("/page", "staged", null);
            _ = await scope.SaveAsync("/other", "also staged", null);
        }

        Assert.Multiple(() => {
            Assert.That(storage["/page"], Is.Null);
            Assert.That(storage["/other"], Is.Null);
            Assert.That(pages.Revisions.SaveCount, Is.Zero);
        });
    }

    [Test]
    public async Task ACommitOfSeveralWritesReportsOneRowEachAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction transaction = scope.BeginTransaction();
        _ = await scope.SaveAsync("/page", "first", null);
        _ = await scope.SaveAsync("/other", "second", null);
        IReadOnlyList<PageWriteResult> report = await transaction.CommitAsync();

        Assert.Multiple(() => {
            Assert.That(report.Select(row => row.Key), Is.EqualTo(["/page", "/other"]));
            Assert.That(report.Select(row => row.Outcome), Is.EqualTo([PageWriteOutcome.Saved, PageWriteOutcome.Saved]));
            Assert.That(storage["/page"]?.Template, Is.EqualTo("first"));
            Assert.That(storage["/other"]?.Template, Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task ARefusedOperationWritesNothingAndUndoesWhatWentBeforeItAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        string head = first.Revision.Hash;
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "moved on");
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction transaction = scope.BeginTransaction();
        _ = await scope.SaveAsync("/other", "second", null);
        _ = await scope.SaveAsync("/page", "third", head);
        IReadOnlyList<PageWriteResult> report = await transaction.CommitAsync();

        Assert.Multiple(() => {
            Assert.That(report[0].Outcome, Is.EqualTo(PageWriteOutcome.RolledBack), "the refusal cost it its write");
            Assert.That(report[1].Outcome, Is.EqualTo(PageWriteOutcome.Stale));
            Assert.That(storage["/other"], Is.Null, "a refusal writes nothing at all");
            Assert.That(storage["/page"]?.Template, Is.EqualTo("moved on"));
        });
    }

    [Test]
    public async Task ACreateAMoveAndADeleteOfOneKeyInATransactionAllSucceedAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction transaction = scope.BeginTransaction();
        PageEditResult created = await scope.SaveAsync("/a", "first", null);
        PageMoveResult moved = await scope.MoveAsync("/a", "/b", created.Hash!);
        PageDeleteResult deleted = await scope.DeleteAsync("/b");
        IReadOnlyList<PageWriteResult> report = await transaction.CommitAsync();

        Assert.Multiple(() => {
            Assert.That(report.Select(row => row.Outcome),
                Is.EqualTo([PageWriteOutcome.Saved, PageWriteOutcome.Moved, PageWriteOutcome.Deleted]),
                "each operation sees what the ones before it wrote");
            Assert.That(storage["/a"], Is.Null, "nothing is left at the old key");
            Assert.That(storage["/b"], Is.Not.Null, "the head is a tombstone at the key the move left");
            Assert.That((storage.GetHeadAsync(PageHarness.Key("/b"))).Result?.IsDeleted, Is.True);
        });
    }

    [Test]
    public async Task ASecondWriteToOneKeyInATransactionSeesTheFirstAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction transaction = scope.BeginTransaction();
        PageEditResult first = await scope.SaveAsync("/page", "first", null);
        PageEditResult second = await scope.SaveAsync("/page", "second", first.Hash);
        IReadOnlyList<PageWriteResult> report = await transaction.CommitAsync();

        Assert.Multiple(() => {
            Assert.That(report.Select(row => row.Outcome), Is.EqualTo([PageWriteOutcome.Saved, PageWriteOutcome.Saved]));
            Assert.That(storage["/page"]?.Template, Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task AWriteAgainstTheStateBeforeAnEarlierOperationRefusesAndUndoesEverythingAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction transaction = scope.BeginTransaction();
        _ = await scope.SaveAsync("/one", "first", null);
        _ = await scope.SaveAsync("/two", "second", null);
        _ = await scope.SaveAsync("/one", "third", "a-hash-from-before-the-transaction");
        IReadOnlyList<PageWriteResult> report = await transaction.CommitAsync();

        Assert.Multiple(() => {
            Assert.That(report.Select(row => row.Outcome),
                Is.EqualTo([PageWriteOutcome.RolledBack, PageWriteOutcome.RolledBack, PageWriteOutcome.Stale]),
                "the writes before the refusal were applied and undone, not skipped");
            Assert.That(storage["/one"], Is.Null);
            Assert.That(storage["/two"], Is.Null);
        });
    }

    [Test]
    public async Task ALoneWriteJoinsTheTransactionTheRenderHasOpenAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using (IPageTransaction held = scope.BeginTransaction()) {
            _ = await scope.SaveAsync("/batched", "batched", null);
            PageEditResult lone = await scope.SaveAsync("/lone", "lone", null);

            Assert.Multiple(() => {
                Assert.That(lone.Outcome, Is.EqualTo(PageEditOutcome.Staged), "an embedded page's write is part of the page's unit");
                Assert.That(storage["/lone"], Is.Null);
                Assert.That(storage["/batched"], Is.Null);
            });
        }

        Assert.Multiple(() => {
            Assert.That(storage["/batched"], Is.Null, "the page was disposed, so its writes were lost");
            Assert.That(storage["/lone"], Is.Null, "and the embedded page's write went with them");
        });
    }

    [Test]
    public async Task ALoneWriteLandsWhenTheTransactionItJoinedCommitsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using (IPageTransaction held = scope.BeginTransaction()) {
            _ = await scope.SaveAsync("/lone", "lone", null);
            _ = await held.CommitAsync();
        }

        Assert.That(storage["/lone"]?.Template, Is.EqualTo("lone"));
    }

    [Test]
    public async Task ARolledBackTransactionWritesNothingAndSaysSoAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction transaction = scope.BeginTransaction();
        _ = await scope.SaveAsync("/page", "first", null);
        IReadOnlyList<PageWriteResult> report = await transaction.RollbackAsync();

        Assert.Multiple(() => {
            Assert.That(report.Select(row => row.Outcome), Is.EqualTo([PageWriteOutcome.NotAttempted]));
            Assert.That(storage["/page"], Is.Null);
        });
    }

    [Test]
    public async Task ARolledBackNestedTransactionLeavesTheOuterOneAloneAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction outer = scope.BeginTransaction();
        _ = await scope.SaveAsync("/outer", "outer", null);

        await using (IPageTransaction inner = scope.BeginTransaction()) {
            _ = await scope.SaveAsync("/inner", "inner", null);
            _ = await inner.RollbackAsync();
        }

        _ = await outer.CommitAsync();

        Assert.Multiple(() => {
            Assert.That(storage["/outer"]?.Template, Is.EqualTo("outer"));
            Assert.That(storage["/inner"], Is.Null, "the nested rollback took only its own write");
        });
    }

    [Test]
    public async Task ANestedTransactionLandsWithTheOneItNestsInsideAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction outer = scope.BeginTransaction();
        _ = await scope.SaveAsync("/outer", "outer", null);

        await using (IPageTransaction inner = scope.BeginTransaction()) {
            _ = await scope.SaveAsync("/inner", "inner", null);
            IReadOnlyList<PageWriteResult> nested = await inner.CommitAsync();

            Assert.Multiple(() => {
                Assert.That(nested[0].Outcome, Is.EqualTo(PageWriteOutcome.Staged), "nothing is stored until the outer commits");
                Assert.That(storage["/inner"], Is.Null);
            });
        }

        IReadOnlyList<PageWriteResult> report = await outer.CommitAsync();

        Assert.Multiple(() => {
            Assert.That(report.Select(row => row.Key), Is.EqualTo(["/outer", "/inner"]));
            Assert.That(report.Select(row => row.Outcome), Is.EqualTo([PageWriteOutcome.Saved, PageWriteOutcome.Saved]));
            Assert.That(storage["/outer"]?.Template, Is.EqualTo("outer"));
            Assert.That(storage["/inner"]?.Template, Is.EqualTo("inner"));
        });
    }

    [Test]
    public async Task ANestedTransactionIsLostWhenTheOuterOneIsDisposedAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageWritesScope scope = Scope(pages);

        await using (IPageTransaction outer = scope.BeginTransaction()) {
            _ = await scope.SaveAsync("/outer", "outer", null);

            await using (IPageTransaction inner = scope.BeginTransaction()) {
                _ = await scope.SaveAsync("/inner", "inner", null);
                _ = await inner.CommitAsync();
            }
        }

        Assert.Multiple(() => {
            Assert.That(storage["/inner"], Is.Null, "a nested write lands with the outer one or not at all");
            Assert.That(storage["/outer"], Is.Null);
        });
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
            Assert.That(deleted, Is.EqualTo(new PageDeleteResult(PageDeleteOutcome.Deleted, true)));
            Assert.That(missing, Is.EqualTo(new PageDeleteResult(PageDeleteOutcome.Missing, false)));
            Assert.That(tombstoned, Is.True);
        });
    }

    [Test]
    public async Task TheEditorKeyIsAnOrdinaryKeyAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        _ = await pages.Editor.SaveAsync(PageHarness.Key(EditorPath), "text");

        PageDeleteResult deleted = await Scope(pages, path: EditorPath).DeleteAsync(EditorPath);

        Assert.That(deleted, Is.EqualTo(new PageDeleteResult(PageDeleteOutcome.Deleted, true)), "the editor's own page is deletable now");
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

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("<% ctx.Response.Write(\"Hello.\"); %>");

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.HasWarnings, Is.False);
        Assert.That(result.Hash, Is.EqualTo(PageHarness.Stored(storage, "/self").Hash));
        Assert.That(PageHarness.Stored(storage, "/self").Template, Is.EqualTo("<% ctx.Response.Write(\"Hello.\"); %>"));
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
    public async Task APageMaySaveItselfTwiceInOneRenderAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope writes = Scope(pages, path: "/self");

        PageSaveResult first = await writes.SaveAsync("First.");
        PageSaveResult second = await writes.SaveAsync("Second.");

        Assert.Multiple(() => {
            Assert.That(first.Hash, Is.Not.Empty);
            Assert.That(second.Hash, Is.Not.Empty.And.Not.EqualTo(first.Hash));
            Assert.That(storage["/self"]?.Template, Is.EqualTo("Second."));
            Assert.That(pages.Revisions.SaveCount, Is.EqualTo(2));
        });
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
    public async Task AnErrorCarriesTheExtentOfTheTokenItNamesAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("<% ctx.Missing(); %>");

        PageDiagnostic error = result.Diagnostics.Single(diagnostic => diagnostic.Severity == PageDiagnosticSeverity.Error);
        Assert.Multiple(() => {
            Assert.That(error.Line, Is.EqualTo(1));
            Assert.That(error.Column, Is.EqualTo(8));
            Assert.That(error.EndLine, Is.EqualTo(1));
            Assert.That(error.EndColumn, Is.EqualTo(15));
            Assert.That(error.Source, Is.Null, "the page's own diagnostic names no key");
        });
    }

    [Test]
    public async Task ADiagnosticFromALibraryNamesTheLibraryAndItsExtentAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        pages.ReadStore.Save(
            "/_lib/document",
            "<%! namespace Site { public static class Document { public static string Read() => Missing(); } } %>",
            templateKind: PageKind.Library);

        PageSaveResult result = await Scope(pages, path: "/self")
            .SaveAsync("<%! using Site; %><% ctx.Response.Write(Document.Read()); %>");

        PageDiagnostic error = result.Diagnostics.Single(diagnostic => diagnostic.Code == "CS0103");
        Assert.Multiple(() => {
            Assert.That(error.Source, Is.EqualTo("/_lib/document"));
            Assert.That(error.Line, Is.EqualTo(1));
            Assert.That(error.Column, Is.EqualTo(84));
            Assert.That(error.EndLine, Is.EqualTo(1));
            Assert.That(error.EndColumn, Is.EqualTo(91));
        });
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

        PageSaveResult result = await Scope(pages, path: "/self").SaveAsync("<% ctx.Response.Write(\"Hello.\"); %>");

        PageDiagnostic error = result.Diagnostics.Single();
        Assert.That(error.Line, Is.Null);
        Assert.That(error.Column, Is.Null);
        Assert.That(error.EndLine, Is.Null);
        Assert.That(error.EndColumn, Is.Null);
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
            () => Scope(pages, path: "/self").SaveAsync("<% ctx.Response.Write(\"Hello.\"); %>"));

        Assert.That(PageHarness.Stored(storage, "/self").Template, Is.EqualTo("<% ctx.Response.Write(\"Hello.\"); %>"));
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
    public async Task TheBoundSaveAndTheNamedSaveAreTheSamePathAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageWritesScope writes = Scope(pages, path: "/self");

        _ = await writes.SaveAsync("first");
        PageEditResult named = await writes.SaveAsync("/other", "second", null);

        Assert.Multiple(() => {
            Assert.That(named.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(storage["/self"]?.Template, Is.EqualTo("first"));
            Assert.That(storage["/other"]?.Template, Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task AMoveMovesThePageAndAnswersItsHashAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);

        PageMoveResult moved = await scope.MoveAsync("/page", "/moved", first.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(moved.Outcome, Is.EqualTo(PageMoveOutcome.Moved));
            Assert.That(moved.Hash, Is.EqualTo(first.Revision.Hash));
            Assert.That(storage["/page"], Is.Null);
            Assert.That(storage["/moved"]?.Template, Is.EqualTo("first"));
            Assert.That(moved.Below, Is.Empty);
        });
    }

    [Test]
    public async Task AnAnonymousMoveIsRefusedBeforeAnythingIsMovedAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");

        await Assert.ThrowsAsync<UserFacingException>(
            async () => await Scope(pages, signedIn: false).MoveAsync("/page", "/moved", first.Revision.Hash));

        Assert.That(storage.WriteCount, Is.EqualTo(1), "only the setup save wrote");
    }

    [Test]
    public async Task AHeadCannotMoveAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Scope(pages, writable: false).MoveAsync("/page", "/moved", first.Revision.Hash));

        Assert.That(storage["/page"]?.Template, Is.EqualTo("first"));
    }

    [Test]
    public async Task AnAbandonedRenderCannotMoveAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);
        scope.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await scope.MoveAsync("/page", "/moved", first.Revision.Hash));
    }

    [Test]
    public async Task ADeadlineThatPassedCannotMoveAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);
        scope.AttachDeadline(new CancellationToken(canceled: true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await scope.MoveAsync("/page", "/moved", first.Revision.Hash));
    }

    [Test]
    public async Task AMoveAndASaveInOneTransactionBothLandAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageWritesScope scope = Scope(pages);

        await using IPageTransaction transaction = scope.BeginTransaction();
        _ = await scope.MoveAsync("/page", "/moved", first.Revision.Hash);
        _ = await scope.SaveAsync("/other", "text", null);
        IReadOnlyList<PageWriteResult> report = await transaction.CommitAsync();

        Assert.Multiple(() => {
            Assert.That(report.Select(row => row.Outcome), Is.EqualTo([PageWriteOutcome.Moved, PageWriteOutcome.Saved]));
            Assert.That(storage["/page"], Is.Null);
            Assert.That(storage["/moved"]?.Template, Is.EqualTo("first"));
            Assert.That(storage["/other"]?.Template, Is.EqualTo("text"));
        });
    }

    [Test]
    public async Task AShellFileCannotBeMovedAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Scope(pages).MoveAsync("/page", "/login", first.Revision.Hash));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Scope(pages).MoveAsync("/login", "/page", first.Revision.Hash));

        Assert.That(storage["/page"]?.Template, Is.EqualTo("first"));
    }

    [Test]
    public async Task APageCannotMoveItselfAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/self"), "first");

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Scope(pages, path: "/self").MoveAsync("/self", "/moved", first.Revision.Hash));

        Assert.Multiple(() => {
            Assert.That(refused?.Message, Does.Contain("cannot move itself"));
            Assert.That(storage["/self"]?.Template, Is.EqualTo("first"));
        });
    }

    [Test]
    public async Task AMoveToTheSameKeyChangesNothingAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        int writes = storage.WriteCount;

        PageMoveResult same = await Scope(pages).MoveAsync("/page", "/page", first.Revision.Hash);
        PageMoveResult caseOnly = await Scope(pages).MoveAsync("/page", "/PAGE", first.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(same.Outcome, Is.EqualTo(PageMoveOutcome.Unchanged));
            Assert.That(caseOnly.Outcome, Is.EqualTo(PageMoveOutcome.Unchanged), "a key is lowercase by construction");
            Assert.That(same.Hash, Is.Null);
            Assert.That(storage.WriteCount, Is.EqualTo(writes));
        });
    }

    [Test]
    public async Task AMoveOfAMissingPageIsNotFoundAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageMoveResult moved = await Scope(pages).MoveAsync("/missing", "/moved", "any-hash");

        Assert.That(moved.Outcome, Is.EqualTo(PageMoveOutcome.SourceNotFound));
    }

    [Test]
    public async Task AMoveOntoALivePageAnswersTargetExistsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision source = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "source");
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/other"), "occupied");

        PageMoveResult moved = await Scope(pages).MoveAsync("/page", "/other", source.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(moved.Outcome, Is.EqualTo(PageMoveOutcome.TargetExists));
            Assert.That(moved.Hash, Is.Null);
            Assert.That(storage["/other"]?.Template, Is.EqualTo("occupied"));
        });
    }

    [Test]
    public async Task AMoveOfAKeyWithPagesBelowItNamesThemAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision source = await pages.Editor.SaveAsync(PageHarness.Key("/folder"), "folder");
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/folder/child"), "child");

        PageMoveResult moved = await Scope(pages).MoveAsync("/folder", "/moved", source.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(moved.Outcome, Is.EqualTo(PageMoveOutcome.FolderNotEmpty));
            Assert.That(moved.Below, Is.EqualTo<string[]>(["/folder/child"]));
        });
    }

    [Test]
    public async Task ALibraryCannotBeMovedAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PagePath key = PageHarness.Key("/_lib/thing");
        storage["/_lib/thing"] = new PageTemplate(key, "public static class Thing { }") { Kind = PageKind.Library };
        string hash = PageHarness.Stored(storage, "/_lib/thing").Hash;

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Scope(pages).MoveAsync("/_lib/thing", "/_lib/other", hash));

        Assert.That(refused?.Message, Does.Contain("library"));
    }

    [Test]
    public async Task OnlyAMoveAnswersWithAHashAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/other"), "occupied");

        PageMoveResult stale = await Scope(pages).MoveAsync("/page", "/third", "not-the-head");
        PageMoveResult target = await Scope(pages).MoveAsync("/page", "/other", first.Revision.Hash);
        PageMoveResult missing = await Scope(pages).MoveAsync("/missing", "/third", first.Revision.Hash);
        PageMoveResult moved = await Scope(pages).MoveAsync("/page", "/third", first.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(stale.Hash, Is.Null);
            Assert.That(target.Hash, Is.Null);
            Assert.That(missing.Hash, Is.Null);
            Assert.That(moved.Hash, Is.EqualTo(first.Revision.Hash));
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
        TestOptions.Seeds,
        new PageTransactionGate());

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
