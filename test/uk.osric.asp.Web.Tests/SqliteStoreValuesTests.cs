// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// Each test owns its storage - a database file of its own, or a store and harness of its own - so the
// tests here are independent, and waiting for one to finish before starting the next only makes the
// suite slower than the work it is doing.
[Parallelizable(ParallelScope.All)]
public sealed class SqliteStoreValuesTests {
    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("values");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("values");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task AValueSurvivesReopeningTheDatabaseAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        await SetAsync(Values(), "/page.cs", "visits", "3");

        StoreValueRow? row = await Values().FindAsync(Subjects("/page.cs"), "/page.cs", "visits");
        Assert.That(row?.Value, Is.EqualTo("3"));
    }

    [Test]
    public async Task AMissingKeyReadsAsNullAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");

        Assert.That(await Values().FindAsync(Subjects("/page.cs"), "/page.cs", "absent"), Is.Null);
    }

    [Test]
    public async Task KeysComeBackInOrdinalOrderAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        IStoreValues store = Values();
        await SetAsync(store, "/page.cs", "b", "1");
        await SetAsync(store, "/page.cs", "A", "1");
        await SetAsync(store, "/page.cs", "a", "1");

        IReadOnlyList<StoreValueRow> rows = await store.ListAsync(Subjects("/page.cs"), "/page.cs", null, 10);
        Assert.That(rows.Select(row => row.Key), Is.EqualTo(new List<string> { "A", "a", "b" }));
    }

    [Test]
    public async Task DeleteReportsWhetherTheKeyExistedAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        IStoreValues store = Values();
        await SetAsync(store, "/page.cs", "key", "1");

        Assert.That(await store.DeleteAsync(Subjects("/page.cs"), "/page.cs", "key"), Is.True);
        Assert.That(await store.DeleteAsync(Subjects("/page.cs"), "/page.cs", "key"), Is.False);
    }

    [Test]
    public async Task PagesAreIsolatedAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/first.cs");
        await DatabaseTemplate.SavePageAsync(_options, "/second.cs");
        IStoreValues store = Values();
        await SetAsync(store, "/first.cs", "shared", "one");

        Assert.That(await store.FindAsync(Subjects("/second.cs"), "/second.cs", "shared"), Is.Null);
        Assert.That(await store.ListAsync(Subjects("/second.cs"), "/second.cs", null, 10), Is.Empty);
    }

    [Test]
    public async Task AWriteToADeletedPageIsRefusedAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        await using (StorageDbContext database = new(_options)) {
            await new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance).DeletePageAsync(Key("/page.cs"));
        }

        await Assert.ThrowsAsync<ArgumentException>(() => SetAsync(Values(), "/page.cs", "key", "1"));
    }

    [Test]
    public async Task DeletingAPageClearsItsValuesAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        await SetAsync(Values(), "/page.cs", "key", "1");

        await using (StorageDbContext database = new(_options)) {
            await new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance).DeletePageAsync(Key("/page.cs"));
        }

        Assert.That(await Values().ListAsync(Subjects("/page.cs"), "/page.cs", null, 10), Is.Empty);
    }

    [Test]
    public async Task ScrubClearsTheValuesOfATombstoneAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        await SetAsync(Values(), "/page.cs", "key", "1");
        await using (StorageDbContext database = new(_options)) {
            IPageStore pages = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
            await pages.DeletePageAsync(Key("/page.cs"));
            await pages.ScrubPageAsync(Key("/page.cs"));
        }

        await using StorageDbContext check = new(_options);
        Assert.That(await check.Set<StoreValueEntry>().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task MovingAPageReKeysItsValuesAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        await SetAsync(Values(), "/page.cs", "key", "1");
        await using (StorageDbContext database = new(_options)) {
            IPageStore pages = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
            PageMoveStoreResult moved = await pages.MovePageAsync(
                Key("/page.cs"), Key("/moved.cs"), await HeadHashAsync("/page.cs"));
            Assert.That(moved.Outcome, Is.EqualTo(PageMoveStoreOutcome.Moved));
        }

        Assert.That(await Values().FindAsync(Subjects("/page.cs"), "/page.cs", "key"), Is.Null);
        StoreValueRow? row = await Values().FindAsync(Subjects("/moved.cs"), "/moved.cs", "key");
        Assert.That(row?.Value, Is.EqualTo("1"));
    }

    [Test]
    public async Task ResavingADeletedPathStartsWithEmptyValuesAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        await SetAsync(Values(), "/page.cs", "key", "1");
        await using (StorageDbContext database = new(_options)) {
            await new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance).DeletePageAsync(Key("/page.cs"));
        }

        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");

        Assert.That(await Values().ListAsync(Subjects("/page.cs"), "/page.cs", null, 10), Is.Empty);
    }

    [Test]
    public async Task AnEditKeepsTheValuesAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        await SetAsync(Values(), "/page.cs", "key", "1");

        await DatabaseTemplate.SavePageAsync(_options, "/page.cs", "edited");

        Assert.That((await Values().FindAsync(Subjects("/page.cs"), "/page.cs", "key"))?.Value, Is.EqualTo("1"));
    }

    [Test]
    public async Task TheLastWriteWinsAndReplacesTheLifetimeAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        IStoreValues store = Values();

        await SetAsync(store, "/page.cs", "key", "first");
        await SetAsync(store, "/page.cs", "key", "second", StoreLifetimeKind.NextRead);

        StoreValueRow? row = await store.FindAsync(Subjects("/page.cs"), "/page.cs", "key");
        Assert.Multiple(() => {
            Assert.That(row?.Value, Is.EqualTo("second"));
            Assert.That(row?.Lifetime, Is.EqualTo((int)StoreLifetimeKind.NextRead));
        });
    }

    [Test]
    public async Task TheKeyCountCapRefusesTheWriteAndLeavesNoRowAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        StoreSubjectCaps caps = TestOptions.Stores.Page with { MaxKeys = 2 };
        IStoreValues store = Values();

        await SetAsync(store, "/page.cs", "one", "1", caps: caps);
        await SetAsync(store, "/page.cs", "two", "2", caps: caps);
        ArgumentException? refused = await Assert.ThrowsAsync<ArgumentException>(
            () => SetAsync(store, "/page.cs", "three", "3", caps: caps));

        Assert.That(refused?.Message, Does.Contain("2 keys"));
        Assert.That(await store.ListAsync(Subjects("/page.cs"), "/page.cs", null, 10), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task TheSubjectByteCapRefusesTheWriteAndLeavesTheOldValueAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        StoreSubjectCaps caps = TestOptions.Stores.Page with { MaxValueBytes = 4096, MaxTotalBytes = 64 };
        IStoreValues store = Values();

        await SetAsync(store, "/page.cs", "small", "1", caps: caps);
        await Assert.ThrowsAsync<ArgumentException>(
            () => SetAsync(store, "/page.cs", "big", new string('x', 200), caps: caps));

        Assert.That((await store.FindAsync(Subjects("/page.cs"), "/page.cs", "small"))?.Value, Is.EqualTo("1"));
        Assert.That(await store.FindAsync(Subjects("/page.cs"), "/page.cs", "big"), Is.Null);
    }

    [Test]
    public async Task AnUpdateIsAllowedWhenThePageIsAlreadyFullAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        StoreSubjectCaps caps = TestOptions.Stores.Page with { MaxValueBytes = 4096, MaxTotalBytes = 64 };
        IStoreValues store = Values();
        await SetAsync(store, "/page.cs", "key", new string('x', 40), caps: caps);

        await SetAsync(store, "/page.cs", "key", new string('y', 40), caps: caps);

        Assert.That((await store.FindAsync(Subjects("/page.cs"), "/page.cs", "key"))?.Value, Is.EqualTo(new string('y', 40)));
    }

    [Test]
    public async Task TheWriteRefreshesTheStorageGaugeAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        PageStorageRefresh refresh = new();
        IStoreValues store = new SqliteStoreValues(
            DatabaseTemplate.Factory(_options), TestOptions.Stores, TimeProvider.System, refresh);

        await SetAsync(store, "/page.cs", "key", "1");

        // A requested refresh wakes the sampler immediately rather than after its sixty-second wait.
        Task woken = refresh.WaitAsync(CancellationToken.None);
        Assert.That(await Task.WhenAny(woken, Task.Delay(TimeSpan.FromSeconds(2))), Is.SameAs(woken));
    }

    [Test]
    public async Task TheSamplerIncludesValueBytesAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        await SetAsync(Values(), "/page.cs", "key", "value");

        await using SqliteConnection connection = new(new SqliteConnectionStringBuilder {
            DataSource = _databasePath,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        (long withValues, _) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);
        await using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = "DELETE FROM StoreValues";
            await command.ExecuteNonQueryAsync();
        }

        (long withoutValues, _) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);

        // The row adds its subject, page, key and value bytes; the revision bytes are unchanged either way.
        Assert.That(withValues - withoutValues, Is.GreaterThanOrEqualTo(5 + 5 + 3 + 5),
            "the subject, page, key and value bytes must be in the payload");
    }

    [Test]
    public async Task ATakeRemovesTheOneShotAndASecondTakeFindsNothingAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        IStoreValues store = Values();
        await SetAsync(store, "/page.cs", "notice", "\"saved\"", StoreLifetimeKind.NextRead);

        StoreValueRow? taken = await store.TakeAsync(Subjects("/page.cs"), "/page.cs", "notice");

        Assert.That(taken?.Value, Is.EqualTo("\"saved\""));
        Assert.That(await store.TakeAsync(Subjects("/page.cs"), "/page.cs", "notice"), Is.Null);
    }

    [Test]
    public async Task AnExpiredValueIsNeitherFoundNorListedAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        IStoreValues store = Values();
        await store.WriteAsync(
            new StoreValueWrite(
                StoreSubjectKeys.ForPage(Key("/page.cs")),
                "/page.cs",
                "notice",
                (int)StoreLifetimeKind.Page,
                StoreSubjectKeys.Stamp(DateTimeOffset.UtcNow.AddSeconds(-1)),
                "\"old\"",
                RequireLivePage: true),
            TestOptions.Stores.Page);

        Assert.That(await store.FindAsync(Subjects("/page.cs"), "/page.cs", "notice"), Is.Null);
        Assert.That(await store.ListAsync(Subjects("/page.cs"), "/page.cs", null, 10), Is.Empty);
    }

    [Test]
    public async Task TheSweepRemovesExpiredValuesAndStaleBrowsersAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        IStoreValues store = Values();
        await SetAsync(store, "/page.cs", "live", "1");
        await store.WriteAsync(
            new StoreValueWrite(
                StoreSubjectKeys.ForPage(Key("/page.cs")),
                "/page.cs",
                "expired",
                (int)StoreLifetimeKind.Page,
                StoreSubjectKeys.Stamp(DateTimeOffset.UtcNow.AddSeconds(-1)),
                "\"old\"",
                RequireLivePage: true),
            TestOptions.Stores.Page);
        await store.WriteAsync(
            new StoreValueWrite(
                StoreSubjectKeys.ForBrowser("stale"),
                "/page.cs",
                "mode",
                (int)StoreLifetimeKind.Browser,
                ExpiresAt: null,
                "\"vim\"",
                RequireLivePage: false),
            TestOptions.Stores.Visitor.Browser);

        int removed = await store.RemoveExpiredAsync(TimeSpan.FromDays(365));

        Assert.That(removed, Is.EqualTo(1), "the expired row goes; a browser written now is not stale");
        Assert.That(await store.FindAsync(Subjects("/page.cs"), "/page.cs", "live"), Is.Not.Null);
    }

    [Test]
    public async Task TheArchiveCarriesOnlyPageValuesWithThePagesOwnLifetimeAsync() {
        await DatabaseTemplate.SavePageAsync(_options, "/page.cs");
        IStoreValues store = Values();
        await SetAsync(store, "/page.cs", "carried", "1");
        await SetAsync(store, "/page.cs", "one-shot", "1", StoreLifetimeKind.NextRead);
        await store.WriteAsync(
            new StoreValueWrite(
                StoreSubjectKeys.ForPage(Key("/page.cs")),
                "/page.cs",
                "timed",
                (int)StoreLifetimeKind.Page,
                StoreSubjectKeys.Stamp(DateTimeOffset.UtcNow.AddMinutes(5)),
                "1",
                RequireLivePage: true),
            TestOptions.Stores.Page);

        IReadOnlyList<StoreValueRow> values = await store.GetPageValuesAsync(Key("/page.cs"));

        Assert.That(values.Select(row => row.Key), Is.EqualTo(new List<string> { "carried" }));
    }

    private Task SetAsync(
        IStoreValues store,
        string page,
        string key,
        string json,
        StoreLifetimeKind kind = StoreLifetimeKind.Page,
        StoreSubjectCaps? caps = null) =>
        store.WriteAsync(
            new StoreValueWrite(
                StoreSubjectKeys.PagePrefix + page,
                page,
                key,
                (int)kind,
                ExpiresAt: null,
                json,
                RequireLivePage: kind == StoreLifetimeKind.Page),
            caps ?? TestOptions.Stores.Page);

    private IStoreValues Values() => new SqliteStoreValues(
        DatabaseTemplate.Factory(_options), TestOptions.Stores, TimeProvider.System);

    private async Task<string> HeadHashAsync(string path) {
        await using StorageDbContext database = new(_options);
        PageHead head = await database.Set<PageHead>().AsNoTracking().SingleAsync(row => row.Path == path);
        PageRevision revision = await database.Set<PageRevision>().AsNoTracking()
            .SingleAsync(row => row.RevisionId == head.RevisionId && row.Path == path);
        return revision.Hash;
    }

    private static IReadOnlyList<string> Subjects(string page) => [StoreSubjectKeys.PagePrefix + page];

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
