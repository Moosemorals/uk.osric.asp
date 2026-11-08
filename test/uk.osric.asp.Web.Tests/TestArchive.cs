// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Runtime.InteropServices;
using uk.osric.asp.Web.Admin;

/// <summary>Builds archive bytes for the reader/writer fixtures, including deliberately broken ones.</summary>
internal static class TestArchive {

    /// <summary>Builds a well-formed archive from page and state rows, without the writer.</summary>
    public static byte[] Build(
        IEnumerable<(string Path, string Source)> pages,
        IEnumerable<(string PagePath, string Key, string Value)>? state = null) {
        return Archive(connection => {
            CreateTables(connection);

            using SqliteCommand insertPage = connection.CreateCommand();
            insertPage.CommandText = """INSERT INTO "Pages" ("Path", "Source") VALUES ($path, $source)""";
            SqliteParameter path = insertPage.Parameters.Add("$path", SqliteType.Text);
            SqliteParameter source = insertPage.Parameters.Add("$source", SqliteType.Text);
            foreach ((string page, string text) in pages) {
                path.Value = page;
                source.Value = text;
                insertPage.ExecuteNonQuery();
            }

            using SqliteCommand insertState = connection.CreateCommand();
            insertState.CommandText = """INSERT INTO "PageState" ("PagePath", "Key", "Value") VALUES ($p, $k, $v)""";
            SqliteParameter pagePath = insertState.Parameters.Add("$p", SqliteType.Text);
            SqliteParameter key = insertState.Parameters.Add("$k", SqliteType.Text);
            SqliteParameter value = insertState.Parameters.Add("$v", SqliteType.Text);
            foreach ((string page, string stateKey, string stateValue) in state ?? []) {
                pagePath.Value = page;
                key.Value = stateKey;
                value.Value = stateValue;
                insertState.ExecuteNonQuery();
            }
        });
    }

    /// <summary>Runs arbitrary SQL against a fresh in-memory database and serializes the result.</summary>
    /// <param name="fill">The SQL the fixture wants to run.</param>
    /// <param name="applicationId">The <c>application_id</c> to set first.</param>
    /// <param name="version">The <c>user_version</c> to set first.</param>
    public static byte[] Archive(
        Action<SqliteConnection> fill,
        uint applicationId = PageArchiveFormat.ApplicationId,
        long version = PageArchiveFormat.Version) {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        // A crafted archive may hold rows its own foreign key would have refused, so the builder
        // writes them the way a tampered file would.
        Execute(connection, "PRAGMA foreign_keys = OFF");
        Execute(connection, $"PRAGMA application_id = {applicationId.ToString(CultureInfo.InvariantCulture)}");
        Execute(connection, $"PRAGMA user_version = {version.ToString(CultureInfo.InvariantCulture)}");
        Execute(connection, "PRAGMA encoding = 'UTF-8'");

        fill(connection);

        return Serialize(connection);
    }

    /// <summary>Creates the two archive tables.</summary>
    public static void CreateTables(SqliteConnection connection) {
        Execute(connection, PageArchiveFormat.CreatePages);
        Execute(connection, PageArchiveFormat.CreatePageState);
    }

    /// <summary>Serializes an open in-memory database.</summary>
    public static byte[] Serialize(SqliteConnection connection) {
        IntPtr buffer = SQLitePCL.raw.sqlite3_serialize(connection.Handle, "main", out long size, 0);
        Assert.That(buffer, Is.Not.EqualTo(IntPtr.Zero), "SQLite could not serialize the test database");

        try {
            byte[] bytes = new byte[checked((int)size)];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return bytes;
        } finally {
            SQLitePCL.raw.sqlite3_free(buffer);
        }
    }

    /// <summary>Runs one statement, ignoring its result.</summary>
    public static void Execute(SqliteConnection connection, string sql) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Runs one scalar query.</summary>
    public static object? Scalar(SqliteConnection connection, string sql) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}

/// <summary>An archive opened for inspection, holding the pinned buffer the connection reads from.</summary>
internal sealed class OpenArchive : IDisposable {

    private readonly GCHandle _pinned;

    /// <summary>Opens <paramref name="bytes"/> as a read-only in-memory database.</summary>
    /// <param name="bytes">The serialized archive.</param>
    public OpenArchive(byte[] bytes) {
        _pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();

        int result = SQLitePCL.raw.sqlite3_deserialize(
            Connection.Handle,
            "main",
            _pinned.AddrOfPinnedObject(),
            bytes.Length,
            bytes.Length,
            SQLitePCL.raw.SQLITE_DESERIALIZE_READONLY);

        Assert.That(result, Is.EqualTo(SQLitePCL.raw.SQLITE_OK), "the test archive could not be opened");
    }

    /// <summary>The connection reading the archive.</summary>
    public SqliteConnection Connection { get; }

    public void Dispose() {
        Connection.Dispose();
        _pinned.Free();
    }
}
