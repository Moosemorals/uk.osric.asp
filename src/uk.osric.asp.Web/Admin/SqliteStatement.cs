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
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
