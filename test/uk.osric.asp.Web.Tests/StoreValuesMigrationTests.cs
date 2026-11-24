// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using uk.osric.asp.Web.Storage;

[TestFixture]
[Category("Slow")]
public sealed class StoreValuesMigrationTests {

    private const string PreviousMigration = "20261118151514_AddOwnerInterrupts";

    private const string BeforeObjects = "20261122190711_AddStoreMessages";

    private const string BeforeSecrets = "20261123082135_AddStoreObjects";

    private static readonly string[] Migrated = [
        "page:/p|/p|k|1||v|2026-11-20T10:00:00.1230000Z",
        "session:s1|/p|sk|3||sv|2026-11-20T11:00:00.0000000Z",
    ];

    private string _databasePath = string.Empty;

    [SetUp]
    public void CreatePath() =>
        _databasePath = Path.Combine(Path.GetTempPath(), $"store-values-migration-{Guid.NewGuid():N}.db");

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task TheMigrationMovesAPageStateRowAndASessionValueIntoOneTableAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync(PreviousMigration);
        await WriteOldRowsAsync(database);

        await database.Database.MigrateAsync();

        Assert.That(await RowsAsync(database), Is.EquivalentTo(Migrated));
    }

    [Test]
    public async Task TheDownMigrationPutsTheRowsBackInTheTablesTheyCameFromAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync(PreviousMigration);
        await WriteOldRowsAsync(database);
        await database.Database.MigrateAsync();

        await database.Database.MigrateAsync(PreviousMigration);

        await Assert.MultipleAsync(async () => {
            Assert.That(SchemaNames(database), Does.Not.Contain("StoreValues"));
            Assert.That(await ScalarAsync(database, """SELECT "Value" FROM "PageState" WHERE "PagePath" = '/p' AND "Key" = 'k'"""), Is.EqualTo("v"));
            Assert.That(await ScalarAsync(database, """SELECT "Value" FROM "PageSessionValues" WHERE "SessionId" = 's1'"""), Is.EqualTo("sv"));
        });
    }

    [Test]
    public async Task ARebuiltStoreValuesTableStillHoldsBothRowsAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync(PreviousMigration);
        await WriteOldRowsAsync(database);

        await database.Database.MigrateAsync();
        await database.Database.MigrateAsync(PreviousMigration);
        await database.Database.MigrateAsync();

        Assert.That(await RowsAsync(database), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task TheObjectMigrationCreatesItsTableAndIndexesAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync(BeforeObjects);

        await database.Database.MigrateAsync();

        Assert.Multiple(() => {
            Assert.That(SchemaNames(database), Does.Contain("StoreObjects"));
            Assert.That(IndexNames(database), Does.Contain("IX_StoreObjects_Sha"));
        });
    }

    [Test]
    public async Task TheSecretMigrationCreatesItsTableAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync(BeforeSecrets);

        await database.Database.MigrateAsync();

        Assert.That(SchemaNames(database), Does.Contain("StoreSecrets"));
    }

    [Test]
    public async Task TheDownSecretMigrationRemovesTheTableAndUpRebuildsItAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync(BeforeSecrets);
        await database.Database.MigrateAsync();

        await database.Database.MigrateAsync(BeforeSecrets);
        Assert.That(SchemaNames(database), Does.Not.Contain("StoreSecrets"));

        await database.Database.MigrateAsync();
        Assert.That(SchemaNames(database), Does.Contain("StoreSecrets"));
    }

    [Test]
    public async Task TheDownObjectMigrationRemovesTheTableAndUpRebuildsItAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync(BeforeObjects);
        await database.Database.MigrateAsync();

        await database.Database.MigrateAsync(BeforeObjects);
        Assert.That(SchemaNames(database), Does.Not.Contain("StoreObjects"));

        await database.Database.MigrateAsync();
        Assert.That(SchemaNames(database), Does.Contain("StoreObjects"));
    }

    // A database that already holds both kinds of value, as a real one does: the migration has to
    // carry the rows across rather than only to build the table.
    private static async Task WriteOldRowsAsync(StorageDbContext database) {
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "PageState" ("PagePath", "Key", "UpdatedAt", "Value")
            VALUES ('/p', 'k', '2026-11-20 10:00:00.1234567+00:00', 'v')
            """);
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "PageSessions" ("SessionId", "Account", "CreatedAt", "UpdatedAt")
            VALUES ('s1', NULL, '2026-11-20 11:00:00', '2026-11-20 11:00:00')
            """);
        await database.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "PageSessionValues" ("SessionId", "PagePath", "Key", "Value")
            VALUES ('s1', '/p', 'sk', 'sv')
            """);
    }

    private static async Task<string[]> RowsAsync(StorageDbContext database) {
        await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != ConnectionState.Open) {
            await command.Connection.OpenAsync();
        }

        command.CommandText = """
            SELECT "Subject", "PageKey", "Key", "Lifetime", COALESCE("ExpiresAt", ''), "Value", "UpdatedAt"
            FROM "StoreValues"
            ORDER BY "Subject"
            """;
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        List<string> rows = [];
        while (await reader.ReadAsync()) {
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetString(index))));
        }

        return [.. rows];
    }

    private static async Task<string?> ScalarAsync(StorageDbContext database, string sql) => (string?)await database
        .Database.SqlQueryRaw<string>(sql).SingleAsync();

    private static string[] SchemaNames(StorageDbContext database) => [.. database.Database
        .SqlQueryRaw<string>("SELECT \"name\" AS \"Value\" FROM sqlite_master WHERE \"type\" IN ('table', 'trigger')")
        .ToArray()];

    private static string[] IndexNames(StorageDbContext database) => [.. database.Database
        .SqlQueryRaw<string>("SELECT \"name\" AS \"Value\" FROM sqlite_master WHERE \"type\" = 'index'")
        .ToArray()];
}
