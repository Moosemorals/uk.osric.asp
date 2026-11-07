// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The schema a storage test starts from: migrated once, then copied for each test.</summary>
/// <remarks>
/// Applying the migrations costs about fifty milliseconds, and the suite needs a database per test,
/// so a fixture migrates one file and hands every test a copy of it. Each test still writes to a file
/// of its own, which is what keeps the fixtures independent and lets them run beside each other.
/// </remarks>
internal sealed class DatabaseTemplate : IDisposable {

    private readonly string _path;

    /// <summary>Migrates a template database, which the fixture holds for its lifetime.</summary>
    /// <param name="name">Names the file, so a stray one says which fixture made it.</param>
    public DatabaseTemplate(string name) {
        _path = Path.Combine(Path.GetTempPath(), $"{name}-template-{Guid.NewGuid():N}.db");

        using StorageDbContext database = new(Options(_path));
        database.Database.Migrate();
    }

    /// <summary>The connection string for a context over <paramref name="path"/>.</summary>
    /// <param name="path">The database file.</param>
    /// <returns>A connection string that leaves pooling off, so the file can be deleted afterwards.</returns>
    public static string ConnectionString(string path) {
        return new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
    }

    /// <summary>The options a context over <paramref name="path"/> is built with.</summary>
    /// <param name="path">The database file.</param>
    /// <returns>The options for <paramref name="path"/>.</returns>
    public static DbContextOptions<StorageDbContext> Options(string path) {
        return new DbContextOptionsBuilder<StorageDbContext>().UseSqlite(ConnectionString(path)).Options;
    }

    /// <summary>A factory over <paramref name="options"/>, for the stores that open their own context.</summary>
    /// <param name="options">The options every context is built with.</param>
    public static IDbContextFactory<StorageDbContext> Factory(DbContextOptions<StorageDbContext> options) {
        return new ContextFactory(options);
    }

    /// <summary>Stores a page through the real store, as a fixture's starting point.</summary>
    /// <param name="options">The options for the database the page is written to.</param>
    /// <param name="path">The page's key.</param>
    /// <param name="template">The template to store.</param>
    public static async Task SavePageAsync(
        DbContextOptions<StorageDbContext> options,
        string path,
        string template = "Hello.") {
        await using StorageDbContext database = new(options);
        await new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance).SavePageAsync(new PageTemplate(PagePath.Normalize(path), template));
    }

    /// <summary>Deletes a database file and the sidecars SQLite writes beside it.</summary>
    /// <param name="path">The database file.</param>
    public static void DeleteFiles(string path) {
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }

    /// <summary>Copies the migrated schema into a file the caller owns.</summary>
    /// <param name="name">Names the file, so a stray one says which fixture made it.</param>
    /// <returns>The path of the copy.</returns>
    public string CreateFile(string name) {
        string path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.db");
        File.Copy(_path, path);

        return path;
    }

    /// <inheritdoc/>
    public void Dispose() => DeleteFiles(_path);

    private sealed class ContextFactory(DbContextOptions<StorageDbContext> options) : IDbContextFactory<StorageDbContext> {
        public StorageDbContext CreateDbContext() => new(options);
    }
}
