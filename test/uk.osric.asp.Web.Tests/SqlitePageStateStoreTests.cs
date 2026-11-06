// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
// Each test migrates its own database, so the per-test paths need a per-test instance to hold them.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqlitePageStateStoreTests {
    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [SetUp]
    public async Task SetUpAsync() {
        _databasePath = Path.Combine(Path.GetTempPath(), $"state-{Guid.NewGuid():N}.db");
        SqliteConnectionStringBuilder connection = new() { DataSource = _databasePath, Pooling = false };
        _options = new DbContextOptionsBuilder<StorageDbContext>().UseSqlite(connection.ToString()).Options;
        await using StorageDbContext database = new(_options);
        await database.Database.MigrateAsync();
    }

    [TearDown]
    public void TearDown() {
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    [Test]
    public async Task AValueSurvivesReopeningTheDatabaseAsync() {
        await SavePageAsync("/page.cs");

        await Store().SetValueAsync(Key("/page.cs"), "visits", "3");

        Assert.That(await Store().GetValueAsync(Key("/page.cs"), "visits"), Is.EqualTo("3"));
    }

    [Test]
    public async Task AMissingKeyReadsAsNullAsync() {
        await SavePageAsync("/page.cs");

        Assert.That(await Store().GetValueAsync(Key("/page.cs"), "absent"), Is.Null);
    }

    [Test]
    public async Task KeysComeBackInOrdinalOrderAsync() {
        await SavePageAsync("/page.cs");
        IPageStateStore store = Store();
        await store.SetValueAsync(Key("/page.cs"), "b", "1");
        await store.SetValueAsync(Key("/page.cs"), "A", "1");
        await store.SetValueAsync(Key("/page.cs"), "a", "1");

        Assert.That(await store.ListKeysAsync(Key("/page.cs")), Is.EqualTo(new List<string> { "A", "a", "b" }));
    }

    [Test]
    public async Task DeleteReportsWhetherTheKeyExistedAsync() {
        await SavePageAsync("/page.cs");
        IPageStateStore store = Store();
        await store.SetValueAsync(Key("/page.cs"), "key", "1");

        Assert.That(await store.DeleteValueAsync(Key("/page.cs"), "key"), Is.True);
        Assert.That(await store.DeleteValueAsync(Key("/page.cs"), "key"), Is.False);
    }

    [Test]
    public async Task PagesAreIsolatedAsync() {
        await SavePageAsync("/first.cs");
        await SavePageAsync("/second.cs");
        IPageStateStore store = Store();
        await store.SetValueAsync(Key("/first.cs"), "shared", "one");

        Assert.That(await store.GetValueAsync(Key("/second.cs"), "shared"), Is.Null);
        Assert.That(await store.ListKeysAsync(Key("/second.cs")), Is.Empty);
    }

    [Test]
    public async Task AWriteToADeletedPageIsRefusedAsync() {
        await SavePageAsync("/page.cs");
        await using (StorageDbContext database = new(_options)) {
            await new SqlitePageStore(database).DeletePageAsync(Key("/page.cs"));
        }

        await Assert.ThrowsAsync<ArgumentException>(() => Store().SetValueAsync(Key("/page.cs"), "key", "1"));
    }

    [Test]
    public async Task DeletingAPageClearsItsStateAsync() {
        await SavePageAsync("/page.cs");
        await Store().SetValueAsync(Key("/page.cs"), "key", "1");

        await using (StorageDbContext database = new(_options)) {
            await new SqlitePageStore(database).DeletePageAsync(Key("/page.cs"));
        }

        Assert.That(await Store().ListKeysAsync(Key("/page.cs")), Is.Empty);
    }

    [Test]
    public async Task ScrubClearsTheStateOfATombstoneAsync() {
        await SavePageAsync("/page.cs");
        await Store().SetValueAsync(Key("/page.cs"), "key", "1");
        await using (StorageDbContext database = new(_options)) {
            IPageStore pages = new SqlitePageStore(database);
            await pages.DeletePageAsync(Key("/page.cs"));
            await pages.ScrubPageAsync(Key("/page.cs"));
        }

        await using StorageDbContext check = new(_options);
        Assert.That(await check.Set<PageStateEntry>().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task ResavingADeletedPathStartsWithEmptyStateAsync() {
        await SavePageAsync("/page.cs");
        await Store().SetValueAsync(Key("/page.cs"), "key", "1");
        await using (StorageDbContext database = new(_options)) {
            await new SqlitePageStore(database).DeletePageAsync(Key("/page.cs"));
        }

        await SavePageAsync("/page.cs");

        Assert.That(await Store().ListKeysAsync(Key("/page.cs")), Is.Empty);
    }

    [Test]
    public async Task AnEditKeepsTheStateAsync() {
        await SavePageAsync("/page.cs");
        await Store().SetValueAsync(Key("/page.cs"), "key", "1");

        await SavePageAsync("/page.cs", "edited");

        Assert.That((await Store().GetValueAsync(Key("/page.cs"), "key")), Is.EqualTo("1"));
    }

    [Test]
    public async Task TheLastWriteWinsAsync() {
        await SavePageAsync("/page.cs");
        IPageStateStore store = Store();

        await store.SetValueAsync(Key("/page.cs"), "key", "first");
        await store.SetValueAsync(Key("/page.cs"), "key", "second");

        Assert.That(await store.GetValueAsync(Key("/page.cs"), "key"), Is.EqualTo("second"));
    }

    [Test]
    public async Task TheKeyCountCapRefusesTheWriteAndLeavesNoRowAsync() {
        await SavePageAsync("/page.cs");
        IPageStateStore store = new SqlitePageStateStore(
            new Factory(_options), new PageStorageOptions { MaxKeysPerPage = 2 }, TimeProvider.System);

        await store.SetValueAsync(Key("/page.cs"), "one", "1");
        await store.SetValueAsync(Key("/page.cs"), "two", "2");
        ArgumentException? refused = await Assert.ThrowsAsync<ArgumentException>(
            () => store.SetValueAsync(Key("/page.cs"), "three", "3"));

        Assert.That(refused?.Message, Does.Contain("2 keys"));
        Assert.That(await store.ListKeysAsync(Key("/page.cs")), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ThePageByteCapRefusesTheWriteAndLeavesTheOldValueAsync() {
        await SavePageAsync("/page.cs");
        IPageStateStore store = new SqlitePageStateStore(
            new Factory(_options),
            new PageStorageOptions { MaxValueBytes = 4096, MaxPageBytes = 64 },
            TimeProvider.System);

        await store.SetValueAsync(Key("/page.cs"), "small", "1");
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SetValueAsync(Key("/page.cs"), "big", new string('x', 200)));

        Assert.That(await store.GetValueAsync(Key("/page.cs"), "small"), Is.EqualTo("1"));
        Assert.That(await store.GetValueAsync(Key("/page.cs"), "big"), Is.Null);
    }

    [Test]
    public async Task AnUpdateIsAllowedWhenThePageIsAlreadyFullAsync() {
        await SavePageAsync("/page.cs");
        IPageStateStore store = new SqlitePageStateStore(
            new Factory(_options),
            new PageStorageOptions { MaxValueBytes = 4096, MaxPageBytes = 64 },
            TimeProvider.System);
        await store.SetValueAsync(Key("/page.cs"), "key", new string('x', 40));

        await store.SetValueAsync(Key("/page.cs"), "key", new string('y', 40));

        Assert.That(await store.GetValueAsync(Key("/page.cs"), "key"), Is.EqualTo(new string('y', 40)));
    }

    [Test]
    public async Task TheWriteRefreshesTheStorageGaugeAsync() {
        await SavePageAsync("/page.cs");
        PageStorageRefresh refresh = new();
        IPageStateStore store = new SqlitePageStateStore(
            new Factory(_options), new PageStorageOptions(), TimeProvider.System, refresh);

        await store.SetValueAsync(Key("/page.cs"), "key", "1");

        // A requested refresh wakes the sampler immediately rather than after its sixty-second wait.
        Task woken = refresh.WaitAsync(CancellationToken.None);
        Assert.That(await Task.WhenAny(woken, Task.Delay(TimeSpan.FromSeconds(2))), Is.SameAs(woken));
    }

    [Test]
    public async Task TheSamplerIncludesStateBytesAsync() {
        await SavePageAsync("/page.cs");
        await Store().SetValueAsync(Key("/page.cs"), "key", "value");

        await using SqliteConnection connection = new(new SqliteConnectionStringBuilder {
            DataSource = _databasePath,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        (long withState, _) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);
        await using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = "DELETE FROM PageState";
            await command.ExecuteNonQueryAsync();
        }

        (long withoutState, _) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);

        // The row adds its four columns' bytes; the revision bytes are unchanged either way.
        Assert.That(withState - withoutState, Is.GreaterThanOrEqualTo(8 + 3 + 5),
            "the path, key and value bytes must be in the payload");
    }

    private IPageStateStore Store() {
        return new SqlitePageStateStore(new Factory(_options), new PageStorageOptions(), TimeProvider.System);
    }

    private async Task SavePageAsync(string path, string template = "Hello.") {
        await using StorageDbContext database = new(_options);
        await new SqlitePageStore(database).SavePageAsync(new PageTemplate(Key(path), template));
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);

    private sealed class Factory(DbContextOptions<StorageDbContext> options) : IDbContextFactory<StorageDbContext> {
        public StorageDbContext CreateDbContext() => new(options);
    }
}
