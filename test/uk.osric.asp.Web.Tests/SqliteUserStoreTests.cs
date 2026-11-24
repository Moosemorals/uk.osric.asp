// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;
using uk.osric.asp.Web.Users;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// Each test owns its storage - a database file of its own, or a store and harness of its own - so the
// tests here are independent, and waiting for one to finish before starting the next only makes the
// suite slower than the work it is doing.
[Parallelizable(ParallelScope.All)]
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
        IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);

        Assert.That(await users.TryAddAsync(User("Osric")), Is.True);
        Assert.That((await users.FindAsync("osric"))?.UserName, Is.EqualTo("osric"));
    }

    [Test]
    public async Task ADuplicateNameIsRefusedAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);
        await users.TryAddAsync(User("owner"));

        Assert.That(await users.TryAddAsync(User("owner")), Is.False);
        Assert.That(await database.Set<LocalUser>().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task MissingNamesReturnNullAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);

        Assert.That(await users.FindAsync("missing"), Is.Null);
    }

    [Test]
    public async Task UpdateHashKeepsTheStampAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);
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
        IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);
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
        IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);

        Assert.That(await users.UpdateHashAsync("missing", "hash"), Is.False);
        Assert.That(await users.ChangePasswordAsync("missing", "hash", "stamp"), Is.False);
    }

    [Test]
    public async Task AccountsAreListedByNameAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);
        await users.TryAddAsync(User("zed"));
        await users.TryAddAsync(User("alpha"));

        IReadOnlyList<LocalUser> accounts = await users.ListAsync();

        Assert.That(accounts.Select(user => user.UserName), Is.EqualTo(["alpha", "zed"]));
    }

    [Test]
    public async Task RemovingAnAccountReturnsWhetherItExistedAsync() {
        await using StorageDbContext database = new(_options);
        IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);
        await users.TryAddAsync(User("owner"));

        Assert.That(await users.RemoveAsync("owner"), Is.True);
        Assert.That(await users.RemoveAsync("owner"), Is.False);
        Assert.That(await users.FindAsync("owner"), Is.Null);
    }

    [Test]
    public async Task RemovingAnAccountLeavesNoRowOfItAnywhereAsync() {
        const string Session = "one";
        await using (StorageDbContext database = new(_options)) {
            _ = await new DeclaredTableApplier(TestOptions.Stores.Records).ApplyAsync(
                database, PagePath.Normalize("/lib"), Notes());
        }

        await InsertNoteAsync("account:owner", "mine");
        await InsertNoteAsync("account:other", "theirs");

        IDbContextFactory<StorageDbContext> contexts = DatabaseTemplate.Factory(_options);
        await new SqliteStoreValues(contexts, TestOptions.Stores, TimeProvider.System).WriteAsync(
            new StoreValueWrite("account:owner", "/lib", "k", (int)StoreLifetimeKind.Person, null, "\"v\"", RequireLivePage: false),
            TestOptions.Stores.Visitor.SignedIn,
            CancellationToken.None);
        _ = await new SqliteStoreMessages(contexts, TestOptions.Stores, TimeProvider.System).PostAsync(
            new StoreMessageWrite("account:owner", "/lib", null, "/lib", "hi", (int)StoreLifetimeKind.Person, null),
            TestOptions.Stores.Messages,
            CancellationToken.None);

        byte[] bytes = "mine"u8.ToArray();
        await new SqliteStoreObjects(contexts, TestOptions.Stores, TimeProvider.System).WriteAsync(
            new StoreObjectWrite(
                "account:owner", PageObjectHash.Of(bytes), bytes.LongLength, "text/plain; charset=utf-8",
                (int)StoreLifetimeKind.Person, null, bytes, RequireLivePage: false),
            TestOptions.Stores.Objects,
            CancellationToken.None);

        await using (StorageDbContext database = new(_options)) {
            database.Set<PageSessionEntry>().Add(new PageSessionEntry {
                SessionId = Session,
                Account = "owner",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            _ = await database.SaveChangesAsync();
        }

        await using (StorageDbContext database = new(_options)) {
            IUserStore users = new SqliteUserStore(database, NullLogger<SqliteUserStore>.Instance);
            _ = await users.TryAddAsync(User("owner"));
            _ = await users.RemoveAsync("owner");
        }

        await using (StorageDbContext database = new(_options)) {
            await Assert.MultipleAsync(async () => {
                Assert.That(await database.Set<StoreValueEntry>().CountAsync(row => row.Subject == "account:owner"), Is.Zero);
                Assert.That(await database.Set<StoreMessageEntry>().CountAsync(row => row.Subject == "account:owner"), Is.Zero);
                Assert.That(await database.Set<StoreObjectEntry>().CountAsync(row => row.Subject == "account:owner"), Is.Zero);
                Assert.That(await database.Set<PageSessionEntry>().CountAsync(row => row.SessionId == Session), Is.Zero);
                Assert.That(await CountNotesAsync("account:owner"), Is.Zero);
                Assert.That(await CountNotesAsync("account:other"), Is.EqualTo(1), "another account's row stays");
                Assert.That(await database.Set<LocalUser>().AnyAsync(), Is.False);
            });
        }
    }

    private async Task InsertNoteAsync(string subject, string title) {
        await using SqliteRecordStore records = NotesFor(subject);
        _ = await records.ExecuteAsync(
            """INSERT INTO "notes" ("Title") VALUES ($title)""",
            new PageParameters().Add("title", title));
    }

    // The handle's view filters by its subject, so this counts the rows one account can still see.
    private async Task<int> CountNotesAsync(string subject) {
        await using SqliteRecordStore records = NotesFor(subject);
        IReadOnlyList<PageRow> rows = await records.QueryAsync("""SELECT COUNT(*) AS "Counted" FROM "notes" """);

        return (int)(rows.Single().GetInt64("Counted") ?? 0);
    }

    private SqliteRecordStore NotesFor(string subject) => new(
        PagePath.Normalize("/lib"),
        _ => subject,
        SqliteRecordStore.SubjectKind.Visitor,
        new SqliteRecordCatalog(DatabaseTemplate.Factory(_options)),
        TestOptions.Stores.Records,
        writable: true);

    private static PageSchema Notes() => new([
        new PageTable(
            "notes",
            StoreTableSubject.Visitor,
            StoreTableLifetime.Person,
            [new PageColumn("Title", PageColumnType.Text)]),
    ]);

    private static LocalUser User(string name) {
        return new LocalUser {
            UserName = UserName.Normalize(name),
            PasswordHash = "hash",
            SecurityStamp = "stamp",
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
