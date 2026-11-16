// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SeedProvenanceTests {

    private static readonly PageSeeds Pack = new(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds"));
    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("seed-provenance");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("seed-provenance");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task AFirstInstallAgreesOnTheSeedTheContentAndTheAnchorAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);

        await PageAssetSeeder.SeedAsync(storage, Pack);

        foreach (SeedFile seed in Pack.Files) {
            PageHeadSummary head = (await storage.GetHeadAsync(seed.Path))!;
            Assert.Multiple(() => {
                Assert.That(head.SeedHash, Is.EqualTo(seed.Hash), seed.Path.Key);
                Assert.That(head.ContentHash, Is.EqualTo(seed.Hash), seed.Path.Key);
                Assert.That(head.ContentHash, Is.EqualTo(TemplateFingerprint.Of(seed.Source)), seed.Path.Key);
            });
        }
    }

    [Test]
    public async Task ASecondInstallWritesNoRevisionAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
        await PageAssetSeeder.SeedAsync(storage, Pack);
        int revisions = await database.Set<PageRevision>().CountAsync();

        await PageAssetSeeder.SeedAsync(storage, Pack);

        Assert.That(await database.Set<PageRevision>().CountAsync(), Is.EqualTo(revisions));
        Assert.That(revisions, Is.EqualTo(Pack.Files.Count));
    }
}
