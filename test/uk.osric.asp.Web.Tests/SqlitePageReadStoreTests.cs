// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

        IReadOnlyList<PageReadHead> rows = await ReadStore().ListAsync("/", 10);

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

        PageReadHead row = (await ReadStore().ListAsync("/", 10)).Single();

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

        IReadOnlyList<PageReadHead> rows = await ReadStore().ListAsync("/", 2);

        Assert.That(rows.Select(row => row.Key), Is.EqualTo(["/one", "/three"]));
    }

    [Test]
    public async Task APrefixContainingAWildcardIsNotAWildcardAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/100%", "percent");
        await DatabaseTemplate.SavePageAsync(_options, "/100x", "letter");
        await DatabaseTemplate.SavePageAsync(_options, "/100_", "underscore");

        IReadOnlyList<PageReadHead> rows = await ReadStore().ListAsync("/100%", 10);

        Assert.That(rows.Select(row => row.Key), Is.EqualTo(["/100%"]));
    }

    [Test]
    public async Task APrefixListsOnlyItsSubtreeAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "blog");
        await DatabaseTemplate.SavePageAsync(_options, "/blog/post", "post");
        await DatabaseTemplate.SavePageAsync(_options, "/blogroll", "not a child");

        IReadOnlyList<PageReadHead> rows = await ReadStore().ListAsync("/blog", 10);

        Assert.That(rows.Select(row => row.Key), Is.EqualTo(["/blog", "/blog/post"]));
    }

    [Test]
    public async Task TheHeadRevisionTimeAndHashAreTheCurrentOnesAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "first");
        await DatabaseTemplate.SavePageAsync(_options, "/blog", "second");
        await using StorageDbContext database = new(_options);
        IPageStore pages = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
        PageTemplate current = (await pages.GetPageAsync(PagePath.Normalize("/blog")))!;

        PageReadHead row = (await ReadStore().ListAsync("/blog", 10)).Single();

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

        _ = await ReadStore().ListAsync("/", 10);
        _ = await ReadStore().GetSourceAsync("/blog");
        _ = await ReadStore().ListRevisionsAsync("/blog");
        _ = await ReadStore().GetRevisionSourceAsync("/blog", hash);

        Assert.That(await SnapshotAsync(), Is.EqualTo(before));
    }

    private async Task<(int Heads, int Revisions, string Hash, DateTimeOffset SavedAt)> SnapshotAsync() {
        await using StorageDbContext database = new(_options);
        int heads = await database.Set<PageHead>().CountAsync();
        int revisions = await database.Set<PageRevision>().CountAsync();
        PageRevision row = await database.Set<PageRevision>().SingleAsync();

        return (heads, revisions, row.Hash, row.SavedAt);
    }

    private IPageReadStore ReadStore() => new SqlitePageReadStore(DatabaseTemplate.Factory(_options));

    private async Task DeleteAsync(string path) {
        await using StorageDbContext database = new(_options);
        IPageStore store = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);

        Assert.That(await store.DeletePageAsync(PagePath.Normalize(path)), Is.True);
    }
}
