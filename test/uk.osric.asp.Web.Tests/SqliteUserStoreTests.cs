// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using uk.osric.asp.Web.Storage;
using uk.osric.asp.Web.Users;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqliteUserStoreTests {

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("users");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("users");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
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
