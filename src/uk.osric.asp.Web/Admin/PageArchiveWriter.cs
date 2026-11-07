// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Runtime.InteropServices;
using uk.osric.asp.Web.Pages;

/// <summary>Builds a page archive in memory and hands back its serialized bytes.</summary>
/// <remarks>
/// <para>
/// The database is created with <c>Data Source=:memory:</c>, filled in one transaction, and then
/// serialized with <c>sqlite3_serialize</c>, so neither direction needs a temporary file. Rows are
/// written in key order; the file layout itself is SQLite's business and is not promised to be
/// byte-stable between versions.
/// </para>
/// <para>
/// Nothing here reads the site's storage: the caller hands over the pages it has already read, which
/// is what makes a storage failure a readable error page rather than half a download.
/// </para>
/// </remarks>
internal static class PageArchiveWriter {

    /// <summary>Builds the archive and returns the serialized database.</summary>
    /// <param name="pages">The pages to write, ordered or not; the writer sorts by key.</param>
    /// <returns>The bytes of a SQLite database holding <see cref="PageArchiveFormat"/>'s two tables.</returns>
    public static byte[] Write(IReadOnlyList<PageArchiveEntry> pages) {
        ArgumentNullException.ThrowIfNull(pages);

        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        SqliteStatement.Execute(connection, $"PRAGMA application_id = {PageArchiveFormat.ApplicationId.ToString(CultureInfo.InvariantCulture)}");
        SqliteStatement.Execute(connection, $"PRAGMA user_version = {PageArchiveFormat.Version.ToString(CultureInfo.InvariantCulture)}");
        SqliteStatement.Execute(connection, "PRAGMA encoding = 'UTF-8'");
        SqliteStatement.Execute(connection, PageArchiveFormat.CreatePages);
        SqliteStatement.Execute(connection, PageArchiveFormat.CreatePageState);

        using (SqliteTransaction transaction = connection.BeginTransaction()) {
            using (SqliteCommand insertPage = connection.CreateCommand()) {
                insertPage.CommandText = """INSERT INTO "Pages" ("Path", "Source") VALUES ($path, $source)""";
                SqliteParameter path = insertPage.Parameters.Add("$path", SqliteType.Text);
                SqliteParameter source = insertPage.Parameters.Add("$source", SqliteType.Text);

                foreach (PageArchiveEntry page in pages.OrderBy(page => page.Path.Key, StringComparer.Ordinal)) {
                    path.Value = page.Path.Key;
                    source.Value = page.Source;
                    insertPage.ExecuteNonQuery();
                }
            }

            using (SqliteCommand insertState = connection.CreateCommand()) {
                insertState.CommandText = """
                    INSERT INTO "PageState" ("PagePath", "Key", "Value") VALUES ($pagePath, $key, $value)
                    """;
                SqliteParameter pagePath = insertState.Parameters.Add("$pagePath", SqliteType.Text);
                SqliteParameter key = insertState.Parameters.Add("$key", SqliteType.Text);
                SqliteParameter value = insertState.Parameters.Add("$value", SqliteType.Text);

                foreach (PageArchiveEntry page in pages.OrderBy(page => page.Path.Key, StringComparer.Ordinal)) {
                    foreach (PageStateValue state in page.State.OrderBy(state => state.Key, StringComparer.Ordinal)) {
                        pagePath.Value = page.Path.Key;
                        key.Value = state.Key;
                        value.Value = state.Value;
                        insertState.ExecuteNonQuery();
                    }
                }
            }

            transaction.Commit();
        }

        return Serialize(connection);
    }

    // sqlite3_serialize hands back memory SQLite owns, so the copy is what is returned and the
    // buffer is freed either way.
    private static byte[] Serialize(SqliteConnection connection) {
        IntPtr buffer = SQLitePCL.raw.sqlite3_serialize(connection.Handle, "main", out long size, 0);
        if (buffer == IntPtr.Zero) {
            throw new InvalidOperationException("SQLite could not serialize the archive database.");
        }

        try {
            byte[] bytes = new byte[checked((int)size)];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return bytes;
        } finally {
            SQLitePCL.raw.sqlite3_free(buffer);
        }
    }
}
