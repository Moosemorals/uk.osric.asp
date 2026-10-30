// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.WebTests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Storage;

[TestFixture]
public sealed class SqliteStorageTests {
    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [SetUp]
    public async Task SetUpAsync() {
        _databasePath = Path.Combine(Path.GetTempPath(), $"storage-{Guid.NewGuid():N}.db");
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
    public async Task EmptyDatabaseHasNoFilesAsync() {
        await using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);

        IEnumerable<string> paths = await storage.ListFilesAsync();

        Assert.That(paths, Is.Empty);
    }

    [Test]
    public async Task MissingFileReturnsNullAsync() {
        await using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);

        string? content = await storage.GetFileContentsAsync("/missing.cs");

        Assert.That(content, Is.Null);
    }

    [TestCase("")]
    [TestCase("public class Example {}")]
    [TestCase("line one\nline two: \u00e9")]
    public async Task WrittenContentSurvivesReopeningAsync(string content) {
        await using (StorageDbContext writer = new(_options)) {
            IStorage storage = new SqliteStorage(writer);
            await storage.SetFileContentsAsync("/example.cs", content);
        }

        await using StorageDbContext reader = new(_options);
        IStorage reopened = new SqliteStorage(reader);
        string? actual = await reopened.GetFileContentsAsync("/example.cs");

        Assert.That(actual, Is.EqualTo(content));
    }

    [Test]
    public async Task OverwriteKeepsOneFileWithLatestContentAsync() {
        await using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);
        await storage.SetFileContentsAsync("/example.cs", "first");

        await storage.SetFileContentsAsync("/example.cs", "second");

        string[] expectedPaths = ["/example.cs"];
        Assert.That(await storage.GetFileContentsAsync("/example.cs"), Is.EqualTo("second"));
        Assert.That(await storage.ListFilesAsync(), Is.EqualTo(expectedPaths));
    }

    [Test]
    public async Task PathsAreStoredExactlyAndCaseSensitivelyAsync() {
        await using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);

        await storage.SetFileContentsAsync("/Example.cs", "upper");
        await storage.SetFileContentsAsync("/example.cs", "lower");
        await storage.SetFileContentsAsync("/dir/../example.cs", "literal");

        string[] expectedPaths = ["/Example.cs", "/example.cs", "/dir/../example.cs"];
        Assert.That(await storage.ListFilesAsync(), Is.EquivalentTo(expectedPaths));
        Assert.That(await storage.GetFileContentsAsync("/Example.cs"), Is.EqualTo("upper"));
        Assert.That(await storage.GetFileContentsAsync("/example.cs"), Is.EqualTo("lower"));
        Assert.That(await storage.GetFileContentsAsync("/dir/../example.cs"), Is.EqualTo("literal"));
    }

    [Test]
    public async Task ReapplyingMigrationsPreservesDataAsync() {
        await using (StorageDbContext writer = new(_options)) {
            IStorage storage = new SqliteStorage(writer);
            await storage.SetFileContentsAsync("/example.cs", "saved");
        }

        await using StorageDbContext database = new(_options);
        await database.Database.MigrateAsync();

        Assert.That(await database.Database.GetAppliedMigrationsAsync(), Has.Exactly(1).Items);
        Assert.That(await new SqliteStorage(database).GetFileContentsAsync("/example.cs"), Is.EqualTo("saved"));
    }

    [Test]
    public void CancelledOperationsThrowAsync() {
        using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);
        CancellationToken cancelled = new(true);

        Assert.That(async () => await storage.ListFilesAsync(cancelled), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(async () => await storage.GetFileContentsAsync("/example.cs", cancelled), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(async () => await storage.SetFileContentsAsync("/example.cs", "content", cancelled), Throws.InstanceOf<OperationCanceledException>());
    }
}
