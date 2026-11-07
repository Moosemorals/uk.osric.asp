// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
}
