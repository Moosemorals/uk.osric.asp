// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using uk.osric.asp.Core;
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
    public async Task MissingFileReturnsNullAsync() {
        await using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);

        string? content = await storage.GetFileContentsAsync(Key("/missing.cs"));

        Assert.That(content, Is.Null);
    }

    [TestCase("")]
    [TestCase("public class Example {}")]
    [TestCase("line one\nline two: \u00e9")]
    public async Task WrittenContentSurvivesReopeningAsync(string content) {
        await using (StorageDbContext writer = new(_options)) {
            IStorage storage = new SqliteStorage(writer);
            await storage.SetFileContentsAsync(Key("/example.cs"), content);
        }

        await using StorageDbContext reader = new(_options);
        IStorage reopened = new SqliteStorage(reader);
        string? actual = await reopened.GetFileContentsAsync(Key("/example.cs"));

        Assert.That(actual, Is.EqualTo(content));
    }

    [Test]
    public async Task OverwriteKeepsOneFileWithLatestContentAsync() {
        await using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);
        await storage.SetFileContentsAsync(Key("/example.cs"), "first");

        await storage.SetFileContentsAsync(Key("/example.cs"), "second");

        Assert.That(await storage.GetFileContentsAsync(Key("/example.cs")), Is.EqualTo("second"));
        Assert.That(await database.Set<StoredFile>().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task DistinctKeysAreSeparatePagesAsync() {
        await using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);

        await storage.SetFileContentsAsync(Key("/example.cs"), "first");
        await storage.SetFileContentsAsync(Key("/other.cs"), "second");

        Assert.That(await storage.GetFileContentsAsync(Key("/example.cs")), Is.EqualTo("first"));
        Assert.That(await storage.GetFileContentsAsync(Key("/other.cs")), Is.EqualTo("second"));
        Assert.That(await database.Set<StoredFile>().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task ReapplyingMigrationsPreservesDataAsync() {
        await using (StorageDbContext writer = new(_options)) {
            IStorage storage = new SqliteStorage(writer);
            await storage.SetFileContentsAsync(Key("/example.cs"), "saved");
        }

        await using StorageDbContext database = new(_options);
        await database.Database.MigrateAsync();

        Assert.That(await database.Database.GetAppliedMigrationsAsync(), Has.Exactly(1).Items);
        Assert.That(await new SqliteStorage(database).GetFileContentsAsync(Key("/example.cs")), Is.EqualTo("saved"));
    }

    [Test]
    public void CancelledOperationsThrowAsync() {
        using StorageDbContext database = new(_options);
        IStorage storage = new SqliteStorage(database);
        CancellationToken cancelled = new(true);

        Assert.That(async () => await storage.GetFileContentsAsync(Key("/example.cs"), cancelled), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(async () => await storage.SetFileContentsAsync(Key("/example.cs"), "content", cancelled), Throws.InstanceOf<OperationCanceledException>());
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
