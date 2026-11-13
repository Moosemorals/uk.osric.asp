// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageEditorScopeTests {

    private const string EditorPath = "/pages";

    private static readonly PageUser Owner = new("owner");

    [Test]
    public async Task ResolveNormalisesTheKeyAndReportsAShellFileAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageEditorScope scope = Scope(pages);

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
        PageEditorScope scope = Scope(pages, signedIn: false);

        await Assert.ThrowsAsync<UserFacingException>(async () => await scope.SaveAsync("/page", "text", "hash"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AHeadCannotWriteAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageEditorScope scope = Scope(pages, writable: false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/page", "text", "hash"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AShellFileIsRefusedAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        PageEditorScope scope = Scope(pages);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/login", "text", "hash"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AStaleSaveWritesNothingAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        SavedRevision first = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "first");
        PageEditorScope scope = Scope(pages);

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
        PageEditorScope scope = Scope(pages);
        using CancellationTokenSource render = new();
        scope.AttachDeadline(render.Token);

        // The render ends while the save is in flight: a write that carried the render's deadline
        // would already be cancelled by the time the store saw it (DEC-191).
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
        PageEditorScope scope = Scope(pages);

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
        PageEditorScope scope = Scope(pages);

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
        PageEditorScope scope = Scope(pages);

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
        PageEditorScope scope = Scope(pages);

        _ = await scope.SaveAsync("/page", "second", first.Revision.Hash);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/page", "third", "hash"));
    }

    [Test]
    public async Task DeleteTombstonesAndReportsWhatItFoundAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "text");
        PageEditorScope scope = Scope(pages);

        PageDeleteResult deleted = await scope.DeleteAsync("/page");
        PageEditorScope second = Scope(pages);
        PageDeleteResult missing = await second.DeleteAsync("/page");
        bool tombstoned = (await storage.GetHeadAsync(PageHarness.Key("/page")))?.IsDeleted ?? false;

        Assert.Multiple(() => {
            Assert.That(deleted, Is.EqualTo(new PageDeleteResult(true, true)));
            Assert.That(missing, Is.EqualTo(new PageDeleteResult(false, false)));
            Assert.That(tombstoned, Is.True);
        });
    }

    [Test]
    public async Task TheEditorCannotDeleteItselfAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageEditorScope scope = Scope(pages);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.DeleteAsync(EditorPath));
    }

    [Test]
    public async Task TheConfiguredKeyIsTheOneTheEditorCannotDeleteAsync() {
        using PageHarness pages = new(new FakePageStore());
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/desk"), "text");
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/page"), "text");
        PageEditorScope scope = Scope(pages, editorPath: "/desk");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.DeleteAsync("/desk"));
        PageDeleteResult deleted = await scope.DeleteAsync("/page");

        Assert.That(deleted, Is.EqualTo(new PageDeleteResult(true, true)));
    }

    [Test]
    public void TheSeedPathsAreTheShippedFiles() {
        using PageHarness pages = new(new FakePageStore());

        Assert.That(Scope(pages).SeedPaths(),
            Is.EqualTo(PageAssetSeeder.SeedPaths().Select(seed => seed.Key)));
    }

    [Test]
    public async Task InstallingASeedThatDiffersSavesItAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        string seed = (await PageAssetSeeder.ReadSeedTemplateAsync(PageHarness.Key("/about")))!;
        SavedRevision changed = await pages.Editor.SaveAsync(PageHarness.Key("/about"), "changed");
        PageEditorScope scope = Scope(pages);

        PageEditResult? result = await scope.InstallSeedAsync("/about", changed.Revision.Hash);

        Assert.Multiple(() => {
            Assert.That(result?.Outcome, Is.EqualTo(PageEditOutcome.Saved));
            Assert.That(PageHarness.Stored(storage, "/about").Template, Is.EqualTo(seed));
        });
    }

    [Test]
    public async Task InstallingASeedThatMatchesWritesNothingAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        string seed = (await PageAssetSeeder.ReadSeedTemplateAsync(PageHarness.Key("/about")))!;
        _ = await pages.Editor.SaveAsync(PageHarness.Key("/about"), seed);
        int writes = storage.WriteCount;
        PageEditorScope scope = Scope(pages);

        PageEditResult? result = await scope.InstallSeedAsync("/about", "hash");

        Assert.Multiple(() => {
            Assert.That(result?.Outcome, Is.EqualTo(PageEditOutcome.Unchanged));
            Assert.That(storage.WriteCount, Is.EqualTo(writes), "a matching seed is not written");
        });
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
        PageEditorScope scope = Scope(pages);

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
        PageEditorScope scope = Scope(pages);

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
        PageEditorScope scope = Scope(pages);

        Assert.That(await scope.RestoreRevisionAsync("/page", other.Revision.Hash, null), Is.Null);
    }

    [Test]
    public async Task AnAbandonedRenderCannotWriteAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageEditorScope scope = Scope(pages);
        scope.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/page", "text", "hash"));
    }

    [Test]
    public async Task ADeadlineThatPassedCannotWriteAsync() {
        using PageHarness pages = new(new FakePageStore());
        PageEditorScope scope = Scope(pages);
        scope.AttachDeadline(new CancellationToken(canceled: true));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.SaveAsync("/page", "text", "hash"));
    }

    private static PageEditorScope Scope(
        PageHarness pages,
        bool writable = true,
        PageUser? user = null,
        bool signedIn = true,
        string editorPath = EditorPath) => new(
        PageHarness.Key(editorPath),
        pages.Editor,
        editorPath,
        signedIn ? user ?? Owner : null,
        writable,
        new ReservedPaths(ReservedPaths.Default));
}
