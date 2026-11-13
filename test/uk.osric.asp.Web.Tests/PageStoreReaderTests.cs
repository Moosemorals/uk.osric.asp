// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Pages;

/// <summary>What the one read seam answers, and what it refuses to answer.</summary>
[TestFixture]
public sealed class PageStoreReaderTests {

    [Test]
    public async Task TheTreeListsTheStoredKeysAndTheFoldersAboveThemInKeyOrderAsync() {
        FakePageStore storage = new();
        storage["/a/b/c"] = new PageTemplate(PageHarness.Key("/a/b/c"), "deep");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "shallow");
        using PageHarness pages = new(storage);

        IReadOnlyList<PageNode> tree = await Reader(pages).TreeAsync(includeDeleted: false, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(PageKeys(tree), Is.EqualTo<string[]>(["/a/b/c", "/b"]), "the stored pages, in key order");
            Assert.That(tree.Select(node => node.Path.Key), Is.EqualTo(Ordered(tree)), "the whole tree is in key order");
            Assert.That(tree.Where(node => node.IsFolder).Select(node => node.Path.Key),
                Does.Contain("/a").And.Contain("/a/b").And.Contain("/admin"), "a folder exists because a key lives below it");
            Assert.That(tree.Single(node => node.Path.Key == "/a"), Is.EqualTo(new PageNode(
                PagePath.Normalize("/a"), IsFolder: true, IsPage: false, IsShellFile: false, IsDeleted: false, CanWrite: true, State: null)));
        });
    }

    [Test]
    public async Task AReservedKeyIsAShellRowEvenWhenTheStoreHoldsAPageAtItAsync() {
        FakePageStore storage = new();
        storage["/login"] = new PageTemplate(PageHarness.Key("/login"), "shadowed");
        using PageHarness pages = new(storage);

        IReadOnlyList<PageNode> tree = await Reader(pages).TreeAsync(includeDeleted: false, CancellationToken.None);

        PageNode login = tree.Single(node => node.Path.Key == "/login");
        Assert.Multiple(() => {
            Assert.That(login.IsPage, Is.True, "the route is at the key");
            Assert.That(login.IsShellFile, Is.True);
            Assert.That(login.CanWrite, Is.False, "no save is accepted at a shell file");
            Assert.That(login.State, Is.Null, "a shell file is not compiled");
            Assert.That(PageKeys(tree), Does.Not.Contain("/login"), "the stored page is not a second row");
        });
    }

    [Test]
    public async Task APageBeneathAReservedRouteIsNotANodeAsync() {
        FakePageStore storage = new();
        storage["/admin/scrub/anything"] = new PageTemplate(PageHarness.Key("/admin/scrub/anything"), "unreachable");
        using PageHarness pages = new(storage);

        IReadOnlyList<PageNode> tree = await Reader(pages).TreeAsync(includeDeleted: true, CancellationToken.None);

        Assert.That(tree.Select(node => node.Path.Key), Does.Not.Contain("/admin/scrub/anything"),
            "the route answers everything below it, so no page is stored there");
    }

    [Test]
    public async Task ATombstoneIsANodeOnlyWhenDeletedKeysAreAskedForAsync() {
        FakePageStore storage = new();
        storage["/live"] = new PageTemplate(PageHarness.Key("/live"), "alpha");
        storage["/gone"] = new PageTemplate(PageHarness.Key("/gone"), "alpha");
        using PageHarness pages = new(storage);
        await pages.Editor.DeleteAsync(PageHarness.Key("/gone"));
        PageStoreReader reader = Reader(pages);

        IReadOnlyList<PageNode> live = await reader.TreeAsync(includeDeleted: false, CancellationToken.None);
        IReadOnlyList<PageNode> all = await reader.TreeAsync(includeDeleted: true, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(PageKeys(live), Is.EqualTo<string[]>(["/live"]));
            Assert.That(PageKeys(all), Is.EqualTo<string[]>(["/gone", "/live"]));
            Assert.That(all.Single(node => node.Path.Key == "/gone").IsDeleted, Is.True, "the row carries the fact, not the words");
        });
    }

    [Test]
    public async Task AShellRecordIsTheReadOfAReservedKeyAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        PageRead? read = await Reader(pages).ReadAsync(PagePath.Normalize("/login"), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.IsShellFile, Is.True);
            Assert.That(read.Page, Is.Null, "no page is stored there");
            Assert.That(read.CanWrite, Is.False);
            Assert.That(read.Text, Does.Contain("shell file"));
        });
    }

    [Test]
    public async Task AReadCarriesTheHashTheStateAndTheSourceAsync() {
        string template = "<% int unused = 1; %>";
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        storage["/loud"] = new PageTemplate(PageHarness.Key("/loud"), template, new PageImage(
            TemplateFingerprint.Of(template), pages.Editor.RuntimeId, [1], [2],
            [new PageDiagnostic(PageDiagnosticSeverity.Warning, new TemplatePosition(1, 1), "CS0219: unused")]));
        storage.CurrentRuntimeId = pages.Editor.RuntimeId;

        PageRead? read = await Reader(pages).ReadAsync(PageHarness.Key("/loud"), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Text, Is.EqualTo(template));
            Assert.That(read.Page!.Hash, Is.EqualTo(storage["/loud"]!.Hash));
            Assert.That(read.CanWrite, Is.True);
            Assert.That(read.State, Is.EqualTo(PageImageState.CompiledWithWarnings));
        });
    }

    [Test]
    public async Task APageWithNoCodeReadsAsStoredAsSourceAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        // A stored no-code page carries the revision's own verdict, which generation writes beside
        // the image; the listing and the read both judge from that column.
        storage["/plain.txt"] = new PageTemplate(PageHarness.Key("/plain.txt"), "just text", new PageImage(
            TemplateFingerprint.Of("just text"), pages.Editor.RuntimeId, null, null, [], HasCode: false)) {
            TemplateHasCode = false,
        };
        storage.CurrentRuntimeId = pages.Editor.RuntimeId;
        PageStoreReader reader = Reader(pages);

        PageRead? read = await reader.ReadAsync(PageHarness.Key("/plain.txt"), CancellationToken.None);
        IReadOnlyList<PageNode> tree = await reader.TreeAsync(includeDeleted: false, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(read!.State, Is.EqualTo(PageImageState.StoredAsSource), "a read says what the listing says");
            Assert.That(tree.Single(node => node.Path.Key == "/plain.txt").State, Is.EqualTo(PageImageState.StoredAsSource));
        });
    }

    [Test]
    public async Task AKeyThatIsOnlyAFolderHasNothingToReadAsync() {
        FakePageStore storage = new();
        storage["/a/b"] = new PageTemplate(PageHarness.Key("/a/b"), "alpha");
        using PageHarness pages = new(storage);

        PageRead? read = await Reader(pages).ReadAsync(PagePath.Normalize("/a"), CancellationToken.None);

        Assert.That(read, Is.Null);
    }

    [Test]
    public async Task TheSourcesAreThePagesAndShellRecordsInEachScopeInKeyOrderAsync() {
        FakePageStore storage = new();
        storage["/a/one"] = new PageTemplate(PageHarness.Key("/a/one"), "one");
        storage["/a/b/two"] = new PageTemplate(PageHarness.Key("/a/b/two"), "two");
        storage["/b"] = new PageTemplate(PageHarness.Key("/b"), "b");
        using PageHarness pages = new(storage);
        PageStoreReader reader = Reader(pages);

        IReadOnlyList<PageRead> level = await reader.SourcesAsync(
            [new PageScope(PagePath.Normalize("/a"), IncludeSelf: true, Recursive: false)], CancellationToken.None);
        IReadOnlyList<PageRead> deep = await reader.SourcesAsync(
            [new PageScope(PagePath.Normalize("/a"), IncludeSelf: true, Recursive: true)], CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(level.Select(source => source.Path.Key), Is.EqualTo<string[]>(["/a/one"]), "one level reads what is directly below");
            Assert.That(deep.Select(source => source.Path.Key), Is.EqualTo<string[]>(["/a/b/two", "/a/one"]), "a descending scope reads everything below, in key order");
        });
    }

    [Test]
    public async Task TheShellRecordsOfTheReservedKeysAreSourcesAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);

        IReadOnlyList<PageRead> sources = await Reader(pages).SourcesAsync(
            [new PageScope(PagePath.Normalize("/"), IncludeSelf: true, Recursive: true)], CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(sources.Select(source => source.Path.Key), Does.Contain("/login"));
            Assert.That(sources.Single(source => source.Path.Key == "/login").IsShellFile, Is.True);
        });
    }

    [Test]
    public async Task ATombstoneIsNeverASourceAsync() {
        FakePageStore storage = new();
        storage["/live"] = new PageTemplate(PageHarness.Key("/live"), "alpha");
        storage["/gone"] = new PageTemplate(PageHarness.Key("/gone"), "alpha");
        using PageHarness pages = new(storage);
        await pages.Editor.DeleteAsync(PageHarness.Key("/gone"));

        IReadOnlyList<PageRead> sources = await Reader(pages).SourcesAsync(
            [new PageScope(PagePath.Normalize("/"), IncludeSelf: true, Recursive: true)], CancellationToken.None);

        Assert.That(sources.Select(source => source.Path.Key), Does.Contain("/live").And.Not.Contain("/gone"));
    }

    [Test]
    public async Task TheHistoryOfAKeyNoPageWasSavedAtIsEmptyAsync() {
        using PageHarness pages = new(new FakePageStore());

        PageHistory history = await Reader(pages).HistoryAsync(PagePath.Normalize("/nothing"), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(history.Exists, Is.False);
            Assert.That(history.Revisions, Is.Empty);
            Assert.That(history, Is.EqualTo(PageHistory.Missing));
        });
    }

    [Test]
    public async Task TheHistoryCarriesTheHeadAndEveryRevisionNewestFirstAsync() {
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PageHarness.Key("/notes"), "one");
        using PageHarness pages = new(storage);
        await pages.Editor.SaveAsync(PageHarness.Key("/notes"), "two");
        await pages.Editor.SaveAsync(PageHarness.Key("/notes"), "three");
        PageStoreReader reader = Reader(pages);

        PageHistory history = await reader.HistoryAsync(PageHarness.Key("/notes"), CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(history.Exists, Is.True);
            Assert.That(history.IsDeleted, Is.False);
            Assert.That(history.Revisions, Has.Count.EqualTo(3));
            Assert.That(history.Revisions[0].IsCurrent, Is.True, "the newest revision is the head");
            Assert.That(history.Revisions.Select(revision => revision.SavedAt).OrderByDescending(saved => saved),
                Is.EqualTo(history.Revisions.Select(revision => revision.SavedAt)), "newest first");
        });
    }

    [Test]
    public async Task ARevisionOfAnotherPageIsNotARevisionOfThisOneAsync() {
        FakePageStore storage = new();
        storage["/one"] = new PageTemplate(PageHarness.Key("/one"), "one");
        storage["/two"] = new PageTemplate(PageHarness.Key("/two"), "two");
        using PageHarness pages = new(storage);
        PageStoreReader reader = Reader(pages);
        string other = storage["/two"]!.Hash;

        PageRevisionTemplate? elsewhere = await reader.RevisionAsync(PageHarness.Key("/one"), other, CancellationToken.None);
        PageRevisionTemplate? here = await reader.RevisionAsync(PageHarness.Key("/two"), other, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(elsewhere, Is.Null, "a hash addresses a save, and this save belongs to another key");
            Assert.That(here, Is.Not.Null);
        });
    }

    private static PageStoreReader Reader(PageHarness pages) => new(pages.Editor, ShellKernelHarness.Policy);

    private static IEnumerable<string> PageKeys(IReadOnlyList<PageNode> tree) =>
        tree.Where(node => node.IsPage && !node.IsShellFile).Select(node => node.Path.Key);

    private static IEnumerable<string> Ordered(IReadOnlyList<PageNode> tree) =>
        tree.Select(node => node.Path.Key).OrderBy(key => key, StringComparer.Ordinal);
}
