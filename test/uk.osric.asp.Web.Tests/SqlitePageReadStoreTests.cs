// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The SQL behind the page catalog's read seam.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqlitePageReadStoreTests {
    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("read");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("read");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task ALivePageIsReadAndATombstoneIsNotAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "live source");
        await DatabaseTemplate.SavePageAsync(_options, "/gone", "deleted source");
        await DeleteAsync("/gone");

        Assert.That((await ReadStore().GetSourceAsync("/blog"))?.Template, Is.EqualTo("live source"));
        Assert.That(await ReadStore().GetSourceAsync("/gone"), Is.Null);
    }

    [Test]
    public async Task TheListingIncludesTombstonesAndSeedsAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/site.css", "body { color: red; }");
        await DatabaseTemplate.SavePageAsync(_options, "/gone", "deleted source");
        await DeleteAsync("/gone");

        IReadOnlyList<PageReadHead> rows = await RowsAsync("/", after: null, 10);

        Assert.That(rows.Select(row => row.Key), Is.EqualTo(["/gone", "/site.css"]));
        Assert.That(rows.Single(row => row.Key == "/gone").IsDeleted, Is.True);
        Assert.That(rows.Single(row => row.Key == "/site.css").IsDeleted, Is.False);
    }

    [Test]
    public async Task TheListingCarriesTheHeadsImageColumnsAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/compiled", "Hello.");
        await using (StorageDbContext database = new(_options)) {
            PageRevision revision = await database.Set<PageRevision>().SingleAsync(row => row.Path == "/compiled");
            revision.RuntimeId = "build-7";
            revision.Image = [1, 2, 3];
            revision.Diagnostics = "[]";
            await database.SaveChangesAsync();
        }

        PageReadHead row = (await RowsAsync("/", after: null, 10)).Single();

        Assert.Multiple(() => {
            Assert.That(row.RuntimeId, Is.EqualTo("build-7"));
            Assert.That(row.HasImage, Is.True);
            Assert.That(row.HasDiagnostics, Is.False, "an empty diagnostics array is not a warning");
        });
    }

    [Test]
    public async Task TheLimitIsInTheStatementAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/one");
        await DatabaseTemplate.SavePageAsync(_options, "/two");
        await DatabaseTemplate.SavePageAsync(_options, "/three");

        IReadOnlyList<PageReadHead> rows = await RowsAsync("/", after: null, 2);

        Assert.That(rows.Select(row => row.Key), Is.EqualTo(["/one", "/three"]));
    }

    [Test]
    public async Task TheCursorIsExclusiveAndPastTheLastKeyAnswersNothingAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/a");
        await DatabaseTemplate.SavePageAsync(_options, "/b");
        await DatabaseTemplate.SavePageAsync(_options, "/c");

        IReadOnlyList<PageReadHead> rows = await RowsAsync("/", after: "/a", 10);
        IReadOnlyList<PageReadHead> past = await RowsAsync("/", after: "/zzz", 10);

        Assert.Multiple(() => {
            Assert.That(rows.Select(row => row.Key), Is.EqualTo(["/b", "/c"]));
            Assert.That(past, Is.Empty);
        });
    }

    [Test]
    public async Task TheListingCarriesTheClosureTheHeadWasCompiledAgainstAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/uses", "uses");
        await using (StorageDbContext database = new(_options)) {
            PageRevision revision = await database.Set<PageRevision>().SingleAsync(row => row.Path == "/uses");
            revision.TemplateLibraries = "/_lib/document@hash-d";
            await database.SaveChangesAsync();
        }

        PageReadHead row = (await RowsAsync("/", after: null, 10)).Single();

        Assert.That(row.TemplateLibraries, Is.EqualTo("/_lib/document@hash-d"));
    }

    [Test]
    public async Task APrefixContainingAWildcardIsNotAWildcardAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/100%", "percent");
        string token = await ReadStore().VersionAsync("/100%");
        await DatabaseTemplate.SavePageAsync(_options, "/100x", "letter");
        await DatabaseTemplate.SavePageAsync(_options, "/100_", "underscore");

        PageReadSlice slice = await ReadStore().ListAsync("/100%", after: null, 10);

        Assert.Multiple(() => {
            Assert.That(slice.Rows.Select(row => row.Key), Is.EqualTo(["/100%"]));
            Assert.That(slice.Version, Is.EqualTo(token), "the token selects the same literal subtree as the rows");
        });
    }

    [Test]
    public async Task APrefixListsOnlyItsSubtreeAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "blog");
        await DatabaseTemplate.SavePageAsync(_options, "/blog/post", "post");
        await DatabaseTemplate.SavePageAsync(_options, "/blogroll", "not a child");

        IReadOnlyList<PageReadHead> rows = await RowsAsync("/blog", after: null, 10);

        Assert.That(rows.Select(row => row.Key), Is.EqualTo(["/blog", "/blog/post"]));
    }

    [Test]
    public async Task AOneLevelReadAnswersThePrefixAndItsOwnChildrenAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/a", "a");
        await DatabaseTemplate.SavePageAsync(_options, "/a/b", "b");
        await DatabaseTemplate.SavePageAsync(_options, "/a/b/c", "c");

        PageReadSlice one = await ReadStore().ListAsync("/a", after: null, 10, recursive: false);
        PageReadSlice all = await ReadStore().ListAsync("/a", after: null, 10);

        Assert.Multiple(() => {
            Assert.That(one.Rows.Select(row => row.Key), Is.EqualTo(["/a", "/a/b"]));
            Assert.That(all.Rows.Select(row => row.Key), Is.EqualTo(["/a", "/a/b", "/a/b/c"]));
        });
    }

    [Test]
    public async Task AOneLevelSliceStillAnswersTheWholePrefixsTokenAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/a", "a");
        await DatabaseTemplate.SavePageAsync(_options, "/a/b/c", "c");

        PageReadSlice one = await ReadStore().ListAsync("/a", after: null, 10, recursive: false);

        Assert.That(one.Version, Is.EqualTo(await ReadStore().VersionAsync("/a")),
            "the fingerprint covers every head under the prefix, not the slice or the level");
    }

    [Test]
    public async Task TheFolderReadNamesEachChildThatHoldsSomethingBelowItAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/a/b/c", "c");
        await DatabaseTemplate.SavePageAsync(_options, "/a/d", "d");
        await DatabaseTemplate.SavePageAsync(_options, "/a/e/f", "f");

        IReadOnlyList<string> folders = await ReadStore().ListFoldersAsync("/a", after: null, 10);

        Assert.That(folders, Is.EqualTo(["/a/b", "/a/e"]),
            "the prefix's own row is not a child, and a child that holds nothing below it is no folder");
    }

    [Test]
    public async Task TheFolderReadFoldsAKeyUnderTheRootAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/a/b", "b");

        IReadOnlyList<string> folders = await ReadStore().ListFoldersAsync("/", after: null, 10);

        Assert.That(folders, Is.EqualTo(["/a"]));
    }

    [Test]
    public async Task TheFolderReadCountsATombstoneAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/a/b/c", "c");
        await DeleteAsync("/a/b/c");

        IReadOnlyList<string> folders = await ReadStore().ListFoldersAsync("/a", after: null, 10);

        Assert.That(folders, Is.EqualTo(["/a/b"]));
    }

    [Test]
    public async Task TheFolderReadTakesTheCursorAndTheLimitAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/a/b/x");
        await DatabaseTemplate.SavePageAsync(_options, "/a/c/x");
        await DatabaseTemplate.SavePageAsync(_options, "/a/d/x");

        IReadOnlyList<string> after = await ReadStore().ListFoldersAsync("/a", after: "/a/b", 10);
        IReadOnlyList<string> limited = await ReadStore().ListFoldersAsync("/a", after: null, 2);

        Assert.Multiple(() => {
            Assert.That(after, Is.EqualTo(["/a/c", "/a/d"]));
            Assert.That(limited, Is.EqualTo(["/a/b", "/a/c"]));
        });
    }

    [Test]
    public async Task TheFolderReadTreatsAWildcardAsASubtreeAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/100%/x/y");
        await DatabaseTemplate.SavePageAsync(_options, "/1000/x/y");

        IReadOnlyList<string> folders = await ReadStore().ListFoldersAsync("/100%", after: null, 10);

        Assert.That(folders, Is.EqualTo(["/100%/x"]));
    }

    [Test]
    public async Task TheFolderReadFoldsAtTheRightSlashWhateverTheKeyHoldsAsync() {
        // Two astral characters in the prefix: SQLite counts characters and a length measured in C#
        // counts UTF-16 units, so this is the key where the two would disagree.
        await DatabaseTemplate.SavePageAsync(_options, "/🚀🚀/x/y/z");

        IReadOnlyList<string> folders = await ReadStore().ListFoldersAsync("/🚀🚀", after: null, 10);

        Assert.That(folders, Is.EqualTo(["/🚀🚀/x"]));
    }

    [Test]
    public async Task TheHeadRevisionTimeAndHashAreTheCurrentOnesAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "first");
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "second");
        await using StorageDbContext database = new(_options);
        IPageStore pages = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
        PageTemplate current = (await pages.GetPageAsync(PagePath.Normalize("/blog")))!;

        PageReadHead row = (await RowsAsync("/blog", after: null, 10)).Single();

        Assert.That(row.Hash, Is.EqualTo(current.Hash));
        Assert.That(row.SavedAt, Is.EqualTo(current.SavedAt));
    }

    [Test]
    public async Task ARevisionsSourceIsReadByKeyAndHashAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "first");
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "second");
        await using StorageDbContext database = new(_options);
        IPageStore pages = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
        PageTemplate current = (await pages.GetPageAsync(PagePath.Normalize("/blog")))!;
        PageRevisionSummary first = (await pages.ListRevisionsAsync(PagePath.Normalize("/blog")))
            .Single(revision => revision.Hash != current.Hash);

        PageReadSource? source = await ReadStore().GetRevisionSourceAsync("/blog", first.Hash);

        Assert.That(source?.Template, Is.EqualTo("first"));
    }

    [Test]
    public async Task AHashFromOnePageIsNotReadUnderAnotherAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/one", "one");
        await DatabaseTemplate.SavePageAsync(_options, "/two", "two");
        await using StorageDbContext database = new(_options);
        IPageStore pages = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
        PageTemplate one = (await pages.GetPageAsync(PagePath.Normalize("/one")))!;

        Assert.That(await ReadStore().GetRevisionSourceAsync("/two", one.Hash), Is.Null);
    }

    [Test]
    public async Task TheStoreNeverWritesAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "source");
        (int Heads, int Revisions, string Hash, DateTimeOffset SavedAt) before = await SnapshotAsync();
        string hash = before.Hash;

        _ = await RowsAsync("/", after: null, 10);
        _ = await ReadStore().ListFoldersAsync("/", after: null, 10);
        _ = await ReadStore().VersionAsync("/");
        _ = await ReadStore().GetSourceAsync("/blog");
        _ = await ReadStore().ListRevisionsAsync("/blog");
        _ = await ReadStore().GetRevisionSourceAsync("/blog", hash);

        Assert.That(await SnapshotAsync(), Is.EqualTo(before));
    }

    [Test]
    public async Task ASliceAnswersTheTokenOfItsPrefixWithItsRowsAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "blog");
        await DatabaseTemplate.SavePageAsync(_options, "/blog/post", "post");
        await DatabaseTemplate.SavePageAsync(_options, "/notes", "notes");

        PageReadSlice slice = await ReadStore().ListAsync("/blog", after: null, 10);
        string version = await ReadStore().VersionAsync("/blog");

        Assert.Multiple(() => {
            Assert.That(slice.Rows.Select(row => row.Key), Is.EqualTo(["/blog", "/blog/post"]));
            Assert.That(slice.Version, Is.EqualTo(version));
        });
    }

    [Test]
    public async Task TheTokenCoversTheWholePrefixNotJustTheSliceAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/a", "a");
        await DatabaseTemplate.SavePageAsync(_options, "/b", "b");
        PageReadSlice first = await ReadStore().ListAsync("/", after: null, 1);

        await DatabaseTemplate.SavePageAsync(_options, "/z", "z");
        PageReadSlice second = await ReadStore().ListAsync("/", after: null, 1);

        Assert.Multiple(() => {
            Assert.That(second.Rows, Has.Count.EqualTo(1));
            Assert.That(second.Version, Is.Not.EqualTo(first.Version), "a save past the limit still moves the prefix");
        });
    }

    [Test]
    public async Task ASaveOutsideThePrefixLeavesItsTokenWhereItWasAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "blog");
        string before = await ReadStore().VersionAsync("/blog");

        await DatabaseTemplate.SavePageAsync(_options, "/notes", "notes");

        Assert.That(await ReadStore().VersionAsync("/blog"), Is.EqualTo(before));
    }

    [Test]
    public async Task ADeleteMovesTheTokenAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "blog");
        string before = await ReadStore().VersionAsync("/blog");

        await DeleteAsync("/blog");

        Assert.That(await ReadStore().VersionAsync("/blog"), Is.Not.EqualTo(before));
    }

    [Test]
    public async Task AScrubMovesTheTokenAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "blog");
        await DeleteAsync("/blog");
        string before = await ReadStore().VersionAsync("/blog");

        await ScrubAsync("/blog");

        Assert.That(await ReadStore().VersionAsync("/blog"), Is.Not.EqualTo(before));
    }

    [Test]
    public async Task APageMovedIntoThePrefixMovesTheTokenAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "blog");
        await DatabaseTemplate.SavePageAsync(_options, "/notes", "notes");
        string before = await ReadStore().VersionAsync("/blog");

        await MoveAsync("/notes", "/blog/notes");

        Assert.That(await ReadStore().VersionAsync("/blog"), Is.Not.EqualTo(before));
    }

    [Test]
    public async Task APageMovedOutOfThePrefixMovesTheTokenAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "blog");
        await DatabaseTemplate.SavePageAsync(_options, "/blog/post", "post");
        string before = await ReadStore().VersionAsync("/blog");

        await MoveAsync("/blog/post", "/notes/post");

        Assert.That(await ReadStore().VersionAsync("/blog"), Is.Not.EqualTo(before));
    }

    [Test]
    public async Task AnAdoptMovesTheTokenAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "seed text");
        string before = await ReadStore().VersionAsync("/blog");

        await AdoptAsync("/blog", TemplateFingerprint.Of("seed text"));

        Assert.That(await ReadStore().VersionAsync("/blog"), Is.Not.EqualTo(before));
    }

    [Test]
    public async Task AContentHashRepairMovesTheTokenAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "source");
        await ClearContentHashAsync("/blog");
        string before = await ReadStore().VersionAsync("/blog");

        await RepairContentHashAsync();

        Assert.That(await ReadStore().VersionAsync("/blog"), Is.Not.EqualTo(before));
    }

    private async Task<(int Heads, int Revisions, string Hash, DateTimeOffset SavedAt)> SnapshotAsync() {
        await using StorageDbContext database = new(_options);
        int heads = await database.Set<PageHead>().CountAsync();
        int revisions = await database.Set<PageRevision>().CountAsync();
        PageRevision row = await database.Set<PageRevision>().SingleAsync();

        return (heads, revisions, row.Hash, row.SavedAt);
    }

    private IPageReadStore ReadStore() => new SqlitePageReadStore(DatabaseTemplate.Factory(_options));

    private async Task<IReadOnlyList<PageReadHead>> RowsAsync(string prefix, string? after, int limit) =>
        (await ReadStore().ListAsync(prefix, after, limit)).Rows;

    private async Task DeleteAsync(string path) {
        await using StorageDbContext database = new(_options);
        IPageStore store = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);

        Assert.That(await store.DeletePageAsync(PagePath.Normalize(path)), Is.True);
    }

    private async Task ScrubAsync(string path) {
        await using StorageDbContext database = new(_options);
        IPageStore store = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);

        Assert.That(await store.ScrubPageAsync(PagePath.Normalize(path)), Is.True);
    }

    private async Task MoveAsync(string from, string to) {
        await using StorageDbContext database = new(_options);
        IPageStore store = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
        PagePath source = PagePath.Normalize(from);
        PageTemplate page = (await store.GetPageAsync(source))!;

        PageMoveStoreResult moved = await store.MovePageAsync(source, PagePath.Normalize(to), page.Hash);

        Assert.That(moved.Outcome, Is.EqualTo(PageMoveStoreOutcome.Moved));
    }

    private async Task AdoptAsync(string path, string seedHash) {
        await using StorageDbContext database = new(_options);
        IPageStore store = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);

        Assert.That(await store.AdoptSeedAsync(PagePath.Normalize(path), seedHash), Is.True);
    }

    private async Task ClearContentHashAsync(string path) {
        await using StorageDbContext database = new(_options);
        PageHead head = await database.Set<PageHead>().SingleAsync(row => row.Path == path);
        head.ContentHash = null;
        await database.SaveChangesAsync();
    }

    private async Task RepairContentHashAsync() {
        await using StorageDbContext database = new(_options);

        _ = await PageContentHashRepair.RunAsync(database);
    }
}
