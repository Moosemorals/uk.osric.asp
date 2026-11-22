// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using System.Text;
using uk.osric.asp.Core.Pages;

/// <summary>The statements a declared table is made of.</summary>
/// <remarks>
/// A declared table's real name is unguessable, because a page's SQL runs against a temp view and the
/// real name is the one thing the views exist to keep out of its reach. The name is generated once,
/// stored in the catalogue and kept through a move, so the rows never move house for a rename.
/// </remarks>
internal static class DeclaredTableSql {

    /// <summary>The prefix every real table carries, so a scrub can find an orphan.</summary>
    public const string RealNamePrefix = "Declared_";

    /// <summary>Mints a real table name: the prefix and 128 random bits in base64url.</summary>
    /// <returns>The name, 31 characters long.</returns>
    public static string MintRealName() =>
        RealNamePrefix + Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    /// <summary>The name of the FTS5 index that shadows a real table.</summary>
    /// <param name="realName">The real table's name.</param>
    /// <returns>The index's name.</returns>
    public static string SearchName(string realName) => realName + "_search";

    /// <summary>The statement that creates one real table.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <param name="table">The declaration.</param>
    /// <returns>One <c>CREATE TABLE</c>, with an owner column when the rows are a visitor's.</returns>
    public static string CreateTable(string realName, PageTable table) {
        ArgumentNullException.ThrowIfNull(table);

        StringBuilder sql = new();
        sql.Append(CultureInfo.InvariantCulture, $"CREATE TABLE \"{realName}\" (\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT");

        if (table.Subject is StoreTableSubject.Visitor) {
            sql.Append(", \"Owner\" TEXT NOT NULL");
        }

        foreach (PageColumn column in table.Columns) {
            sql.Append(CultureInfo.InvariantCulture,
                $", \"{column.Name}\" {StorageClass(column.Type)}{(column.Nullable ? " NULL" : " NOT NULL")}");
        }

        sql.Append(')');

        return sql.ToString();
    }

    /// <summary>The statement that adds one column to a real table.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <param name="column">The column to add.</param>
    /// <returns>One <c>ALTER TABLE</c>.</returns>
    /// <remarks>A column added to a table with rows needs a default, and SQLite's is the column's type
    /// default: a not-null column gains empty text, zero or false on every existing row.</remarks>
    public static string AddColumn(string realName, PageColumn column) =>
        $"ALTER TABLE \"{realName}\" ADD COLUMN \"{column.Name}\" {StorageClass(column.Type)}"
        + (column.Nullable ? " NULL" : " NOT NULL DEFAULT " + TypeDefault(column.Type));

    /// <summary>The statement that drops one column from a real table.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <param name="column">The column's name.</param>
    /// <returns>One <c>ALTER TABLE</c>.</returns>
    public static string DropColumn(string realName, string column) =>
        $"ALTER TABLE \"{realName}\" DROP COLUMN \"{column}\"";

    /// <summary>The statements that give a visitor's table its owner index.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <returns>One <c>CREATE INDEX</c>.</returns>
    public static string CreateOwnerIndex(string realName) =>
        $"CREATE INDEX \"{realName}_owner\" ON \"{realName}\" (\"Owner\")";

    /// <summary>The statements that build a table's FTS5 index and the three triggers that keep it.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <param name="table">The declaration, whose <see cref="PageTable.Search"/> column is indexed.</param>
    /// <returns>The index and its triggers, as separate statements.</returns>
    /// <remarks>
    /// External content, so the index stores no second copy of the text, and the tokenizer the
    /// assistant's own memory uses: an invariant, because a page that could weaken it would weaken
    /// every search on the site.
    /// </remarks>
    public static IReadOnlyList<string> CreateSearch(string realName, PageTable table) {
        ArgumentNullException.ThrowIfNull(table);

        string search = table.Search
            ?? throw new ArgumentException($"{table.Name} declares no search column.", nameof(table));
        string index = SearchName(realName);

        return [
            $"""
            CREATE VIRTUAL TABLE "{index}" USING fts5(
                "{search}",
                content='{realName}',
                content_rowid='Id',
                tokenize='porter unicode61 remove_diacritics 2'
            )
            """,
            $"""
            CREATE TRIGGER "{realName}_ai" AFTER INSERT ON "{realName}" BEGIN
                INSERT INTO "{index}"(rowid, "{search}") VALUES (new."Id", new."{search}");
            END
            """,
            $"""
            CREATE TRIGGER "{realName}_ad" AFTER DELETE ON "{realName}" BEGIN
                INSERT INTO "{index}"("{index}", rowid, "{search}") VALUES ('delete', old."Id", old."{search}");
            END
            """,
            $"""
            CREATE TRIGGER "{realName}_au" AFTER UPDATE OF "{search}" ON "{realName}" BEGIN
                INSERT INTO "{index}"("{index}", rowid, "{search}") VALUES ('delete', old."Id", old."{search}");
                INSERT INTO "{index}"(rowid, "{search}") VALUES (new."Id", new."{search}");
            END
            """,
        ];
    }

    /// <summary>The statements that replace one table's cap triggers.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <param name="table">The declaration, whose columns and subject decide what the cap counts.</param>
    /// <param name="records">The caps.</param>
    /// <returns>The two triggers, written fresh so a changed cap takes effect on the next save.</returns>
    /// <remarks>
    /// The caps are enforced inside the statement that writes, so a concurrent writer either fits or is
    /// refused and cannot slip over a cap through a read-then-write race in the host. The numbers are
    /// baked into the trigger, which is why every save replaces them: the alternative is a cap that
    /// only applies to tables made after an operator changed it.
    /// </remarks>
    public static IReadOnlyList<string> CreateCaps(string realName, PageTable table, PageRecordOptions records) {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(records);

        string owner = table.Subject is StoreTableSubject.Visitor ? " AND \"Owner\" = NEW.\"Owner\"" : string.Empty;
        string bytes = string.Join(" + ",
            table.Columns.Select(column => $"COALESCE(length(CAST(NEW.\"{column.Name}\" AS BLOB)), 0)"));
        string storedBytes = string.Join(" + ",
            table.Columns.Select(column => $"COALESCE(length(CAST(\"{column.Name}\" AS BLOB)), 0)"));

        string condition = $"""
            (SELECT COUNT(*) FROM "{realName}" WHERE 1 = 1{owner}) >= {records.MaxRowsPerSubject}
              OR (SELECT COALESCE(SUM({storedBytes}), 0) FROM "{realName}" WHERE 1 = 1{owner}) + ({bytes}) > {records.MaxBytesPerSubject}
            """;

        return [
            $"DROP TRIGGER IF EXISTS \"{realName}_cap_insert\"",
            $"""
            CREATE TRIGGER "{realName}_cap_insert" BEFORE INSERT ON "{realName}"
            WHEN {condition}
            BEGIN
                SELECT RAISE(ABORT, 'FULL');
            END
            """,
            $"DROP TRIGGER IF EXISTS \"{realName}_cap_update\"",
            $"""
            CREATE TRIGGER "{realName}_cap_update" BEFORE UPDATE ON "{realName}"
            WHEN {condition}
            BEGIN
                SELECT RAISE(ABORT, 'FULL');
            END
            """,
        ];
    }

    /// <summary>The statements that remove one table's cap triggers.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <returns>The drops.</returns>
    /// <remarks>
    /// A cap trigger names every column, so it has to go before a column is altered and come back
    /// afterwards: SQLite re-parses a trigger when the table it is on changes.
    /// </remarks>
    public static IReadOnlyList<string> DropCaps(string realName) => [
        $"DROP TRIGGER IF EXISTS \"{realName}_cap_insert\"",
        $"DROP TRIGGER IF EXISTS \"{realName}_cap_update\"",
    ];

    /// <summary>The statements that remove one table's FTS5 index and its triggers.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <returns>The drops, triggers before the index they name.</returns>
    public static IReadOnlyList<string> DropSearch(string realName) => [
        $"DROP TRIGGER IF EXISTS \"{realName}_ai\"",
        $"DROP TRIGGER IF EXISTS \"{realName}_ad\"",
        $"DROP TRIGGER IF EXISTS \"{realName}_au\"",
        $"DROP TABLE IF EXISTS \"{SearchName(realName)}\"",
    ];

    /// <summary>The statements that remove every trace of one real table.</summary>
    /// <param name="realName">The table's real name.</param>
    /// <param name="search">Whether the table has an FTS5 index.</param>
    /// <returns>The drops, in the order that leaves nothing behind.</returns>
    public static IReadOnlyList<string> Drop(string realName, bool search) {
        List<string> statements = [.. DropCaps(realName), $"DROP INDEX IF EXISTS \"{realName}_owner\""];

        if (search) {
            statements.AddRange(DropSearch(realName));
        }

        statements.Add($"DROP TABLE IF EXISTS \"{realName}\"");

        return statements;
    }

    /// <summary>The SQLite storage class a declared column holds.</summary>
    /// <param name="type">The column's type.</param>
    /// <returns>The type name SQLite is given.</returns>
    public static string StorageClass(PageColumnType type) => type switch {
        PageColumnType.Text or PageColumnType.Timestamp => "TEXT",
        PageColumnType.WholeNumber or PageColumnType.Flag => "INTEGER",
        PageColumnType.Number => "REAL",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a declared column type."),
    };

    // A column added to a table that already holds rows needs the value its rows get, and the value
    // the four storage classes agree on is the empty one.
    private static string TypeDefault(PageColumnType type) => type switch {
        PageColumnType.Text or PageColumnType.Timestamp => "''",
        PageColumnType.WholeNumber or PageColumnType.Flag => "0",
        PageColumnType.Number => "0",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a declared column type."),
    };
}
