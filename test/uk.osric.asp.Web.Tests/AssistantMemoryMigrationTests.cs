// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Storage;

[TestFixture]
public sealed class AssistantMemoryMigrationTests {

    private const string PreviousMigration = "20261106224330_AddPageState";

    private string _databasePath = string.Empty;

    [SetUp]
    public void CreatePath() =>
        _databasePath = Path.Combine(Path.GetTempPath(), $"assistant-memory-migration-{Guid.NewGuid():N}.db");

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task TheMigrationAppliesToADatabaseAtThePreviousMigrationAndAnswersARecallAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        // A database that already holds a page and a conversation, as a real one does: the migration
        // has to add tables beside them rather than only to an empty file.
        await database.Database.MigrateAsync(PreviousMigration);
        await database.Database.MigrateAsync();

        Assert.That(SchemaNames(database), Does.Contain("AssistantMemorySearch")
            .And.Contain("AssistantMemories_ai")
            .And.Contain("AssistantMemories_ad")
            .And.Contain("AssistantMemories_au"));

        SqliteMemoryStore memories = new(database, new AssistantOptions(), TimeProvider.System);
        await memories.RememberAsync("owner", MemoryKind.Preference, "The owner prefers colour.", new MemorySource(1, "run-1"));
        IReadOnlyList<MemoryCandidate> found = await memories.RecallAsync("owner", "\"colour\"*", 50);

        Assert.That(found, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TheDownMigrationDropsTheTriggersAndTheVirtualTableAsWellAsTheTablesAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync();

        await database.Database.MigrateAsync(PreviousMigration);

        Assert.Multiple(() => {
            Assert.That(SchemaNames(database), Does.Not.Contain("AssistantMemorySearch"));
            Assert.That(SchemaNames(database), Does.Not.Contain("AssistantMemories"));
            Assert.That(SchemaNames(database), Does.Not.Contain("AssistantMemoryEdits"));
            Assert.That(SchemaNames(database), Does.Not.Contain("AssistantMemories_ai")
                .And.Not.Contain("AssistantMemories_ad")
                .And.Not.Contain("AssistantMemories_au"));
        });
    }

    [Test]
    public async Task ARebuiltDatabaseAnswersARecallAgainAsync() {
        await using StorageDbContext database = new(DatabaseTemplate.Options(_databasePath));
        await database.Database.MigrateAsync();
        SqliteMemoryStore memories = new(database, new AssistantOptions(), TimeProvider.System);
        await memories.RememberAsync("owner", MemoryKind.Fact, "The site runs on Podman.", new MemorySource(1, "run-1"));

        await database.Database.MigrateAsync(PreviousMigration);
        await database.Database.MigrateAsync();
        // The rebuilt tables hold none of the rows this context still tracks, so the tracker is emptied
        // rather than told about a row that no longer exists.
        database.ChangeTracker.Clear();

        Assert.That((await memories.RecallAsync("owner", "\"podman\"*", 50)), Is.Empty, "a dropped table takes its rows with it");
        MemoryWriteResult remembered = await memories.RememberAsync("owner", MemoryKind.Fact, "The site runs on Podman.", new MemorySource(1, "run-1"));
        Assert.That(remembered.IsSuccess, Is.True);
        Assert.That((await memories.RecallAsync("owner", "\"podman\"*", 50)), Has.Count.EqualTo(1));
    }

    private static string[] SchemaNames(StorageDbContext database) => [.. database.Database
        .SqlQueryRaw<string>("SELECT \"name\" AS \"Value\" FROM sqlite_master WHERE \"type\" IN ('table', 'trigger')")
        .ToArray()];
}
