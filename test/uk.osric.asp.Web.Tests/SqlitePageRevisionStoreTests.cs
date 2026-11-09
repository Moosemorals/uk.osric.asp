// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The SQL behind the save seam a page's write runs through.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqlitePageRevisionStoreTests {
    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("revision");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("revision");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task ASaveWritesOneRevisionAndMovesTheHeadAsync() {
        PageTemplate first = new(Key("/blog"), "first");
        PageTemplate second = new(Key("/blog"), "second");

        await Store().SaveAsync(first);
        PageTemplate stored = await Store().SaveAsync(second);

        Assert.That(await HeadRevisionAsync("/blog"), Is.EqualTo(stored.RevisionId));
        Assert.That(await TemplateOfAsync(stored.RevisionId), Is.EqualTo("second"));
        Assert.That(await RevisionCountAsync("/blog"), Is.EqualTo(2), "a save appends rather than replaces");
    }

    [Test]
    public async Task TheResultIsTheRevisionTheStoreKeptAsync() {
        PageTemplate template = new(Key("/blog"), "Hello.");

        PageTemplate stored = await Store().SaveAsync(template);

        Assert.That(stored.Hash, Is.EqualTo(template.Hash));
        Assert.That(await HeadRevisionAsync("/blog"), Is.EqualTo(template.RevisionId));
    }

    [Test]
    public async Task AUniqueViolationIsRetriedWithANewRevisionIdAsync() {
        PageTemplate taken = new(Key("/first"), "first");
        await Store().SaveAsync(taken);

        // A second writer picked the same revision id: the insert is refused, the loop regenerates
        // the id, and the author's template is still stored.
        PageTemplate colliding = new PageTemplate(Key("/second"), "second") with {
            RevisionId = taken.RevisionId,
            Hash = RevisionHash.Of("second", "legacy:unknown", DateTimeOffset.UtcNow, taken.RevisionId),
        };

        PageTemplate stored = await Store().SaveAsync(colliding);

        Assert.That(stored.RevisionId, Is.Not.EqualTo(taken.RevisionId));
        Assert.That(stored.Template, Is.EqualTo("second"));
        Assert.That(await HeadRevisionAsync("/second"), Is.EqualTo(stored.RevisionId));
        Assert.That(await TemplateOfAsync(taken.RevisionId), Is.EqualTo("first"), "the first page is untouched");
    }

    [Test]
    public async Task TheSeamAndTheEditorStoreTheSameRowsAsync() {
        PageTemplate throughSeam = new(Key("/seam"), "Hello.");
        PageTemplate throughStore = new(Key("/store"), "Hello.");

        await Store().SaveAsync(throughSeam);
        await using (StorageDbContext database = new(_options)) {
            await new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance).SavePageAsync(throughStore);
        }

        (string Template, string CreationRuntimeId, string? TemplateHash, string? RuntimeId, byte[]? Image, string? Diagnostics)
            seam = await RowShapeAsync(throughSeam.RevisionId);
        (string Template, string CreationRuntimeId, string? TemplateHash, string? RuntimeId, byte[]? Image, string? Diagnostics)
            store = await RowShapeAsync(throughStore.RevisionId);

        Assert.That(seam, Is.EqualTo(store));
        Assert.That(await HeadRevisionAsync("/seam"), Is.EqualTo(throughSeam.RevisionId));
    }

    [Test]
    public async Task ASaveLandsAfterTheRequestsContextIsGoneAsync() {
        PageTemplate template = new(Key("/blog"), "Hello.");

        // A request's own context, disposed before the save: the seam opens its own, so a page's task
        // cannot touch a disposed one (DEC-192).
        StorageDbContext request = new(_options);
        await request.Database.OpenConnectionAsync();
        await request.DisposeAsync();

        await Store().SaveAsync(template);

        Assert.That(await HeadRevisionAsync("/blog"), Is.EqualTo(template.RevisionId));
    }

    private IPageRevisionStore Store() =>
        new SqlitePageRevisionStore(DatabaseTemplate.Factory(_options), NullLogger<SqlitePageRevisionStore>.Instance);

    private async Task<string?> HeadRevisionAsync(string path) {
        await using StorageDbContext database = new(_options);

        return await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == path)
            .Select(head => head.RevisionId)
            .SingleOrDefaultAsync();
    }

    private async Task<string?> TemplateOfAsync(string revisionId) {
        await using StorageDbContext database = new(_options);

        return await database.Set<PageRevision>().AsNoTracking()
            .Where(revision => revision.RevisionId == revisionId)
            .Select(revision => revision.Template)
            .SingleOrDefaultAsync();
    }

    private async Task<int> RevisionCountAsync(string path) {
        await using StorageDbContext database = new(_options);

        return await database.Set<PageRevision>().AsNoTracking().CountAsync(revision => revision.Path == path);
    }

    private async Task<(string Template, string CreationRuntimeId, string? TemplateHash, string? RuntimeId, byte[]? Image, string? Diagnostics)> RowShapeAsync(string revisionId) {
        await using StorageDbContext database = new(_options);
        PageRevision row = await database.Set<PageRevision>().AsNoTracking()
            .SingleAsync(revision => revision.RevisionId == revisionId);

        return (row.Template, row.CreationRuntimeId, row.TemplateHash, row.RuntimeId, row.Image, row.Diagnostics);
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
