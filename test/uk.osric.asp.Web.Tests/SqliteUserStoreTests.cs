// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using uk.osric.asp.Web.Storage;
using uk.osric.asp.Web.Users;

[TestFixture]
// Each test migrates its own database, so the per-test paths need a per-test instance to hold them.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqliteUserStoreTests {

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [SetUp]
    public async Task SetUpAsync() {
        _databasePath = Path.Combine(Path.GetTempPath(), $"users-{Guid.NewGuid():N}.db");
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
    public async Task AnAddedAccountCanBeFoundAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database);

        Assert.That(await users.TryAddAsync(User("Osric")), Is.True);
        Assert.That((await users.FindAsync("osric"))?.UserName, Is.EqualTo("osric"));
    }

    [Test]
    public async Task ADuplicateNameIsRefusedAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database);
        await users.TryAddAsync(User("owner"));

        Assert.That(await users.TryAddAsync(User("owner")), Is.False);
        Assert.That(await database.Set<LocalUser>().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task MissingNamesReturnNullAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database);

        Assert.That(await users.FindAsync("missing"), Is.Null);
    }

    [Test]
    public async Task UpdateHashKeepsTheStampAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database);
        LocalUser original = User("owner");
        await users.TryAddAsync(original);

        Assert.That(await users.UpdateHashAsync("owner", "new-hash"), Is.True);

        LocalUser? updated = await users.FindAsync("owner");
        Assert.That(updated?.PasswordHash, Is.EqualTo("new-hash"));
        Assert.That(updated?.SecurityStamp, Is.EqualTo(original.SecurityStamp));
    }

    [Test]
    public async Task ChangePasswordRotatesTheStampAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database);
        LocalUser original = User("owner");
        await users.TryAddAsync(original);

        Assert.That(await users.ChangePasswordAsync("owner", "new-hash", "new-stamp"), Is.True);

        LocalUser? updated = await users.FindAsync("owner");
        Assert.That(updated?.PasswordHash, Is.EqualTo("new-hash"));
        Assert.That(updated?.SecurityStamp, Is.EqualTo("new-stamp"));
    }

    [Test]
    public async Task UpdateOfAMissingAccountReturnsFalseAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database);

        Assert.That(await users.UpdateHashAsync("missing", "hash"), Is.False);
        Assert.That(await users.ChangePasswordAsync("missing", "hash", "stamp"), Is.False);
    }

    [Test]
    public async Task AccountsAreListedByNameAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database);
        await users.TryAddAsync(User("zed"));
        await users.TryAddAsync(User("alpha"));

        IReadOnlyList<LocalUser> accounts = await users.ListAsync();

        Assert.That(accounts.Select(user => user.UserName), Is.EqualTo(["alpha", "zed"]));
    }

    [Test]
    public async Task RemovingAnAccountReturnsWhetherItExistedAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database);
        await users.TryAddAsync(User("owner"));

        Assert.That(await users.RemoveAsync("owner"), Is.True);
        Assert.That(await users.RemoveAsync("owner"), Is.False);
        Assert.That(await users.FindAsync("owner"), Is.Null);
    }

    private static LocalUser User(string name) {
        return new LocalUser {
            UserName = UserName.Normalize(name),
            PasswordHash = "hash",
            SecurityStamp = "stamp",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
