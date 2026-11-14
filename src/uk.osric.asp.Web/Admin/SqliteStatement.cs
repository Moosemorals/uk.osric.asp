// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.Data.Sqlite;

/// <summary>Runs one statement against an archive's connection.</summary>
internal static class SqliteStatement {

    /// <summary>Executes a statement that returns no rows.</summary>
    /// <param name="connection">The connection to run it on.</param>
    /// <param name="sql">The SQL to run.</param>
    public static void Execute(SqliteConnection connection, string sql) {
        ArgumentNullException.ThrowIfNull(connection);

        using SqliteCommand command = connection.CreateCommand();
        // The statement is a fixed string chosen by the caller, never a value from a request or an archive.
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }
}
