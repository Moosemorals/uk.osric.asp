// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>Applies a source's declaration to the database, inside the transaction that saves it.</summary>
/// <remarks>
/// <para>
/// The declaration is the table. A save creates a table it has not made, adds a column it has not made,
/// and drops a column or a table the source no longer names, with every visitor's rows in it — that is
/// the rule the owner chose, the editor's diff is the warning, and the host never drops anything on its
/// own initiative.
/// </para>
/// <para>
/// Nothing here is speculative: a revision that does not compile applies nothing, and a declaration with
/// a problem is stored with its diagnostics and applies nothing either. A refusal leaves the schema and
/// the rows exactly as they were, because the statements run in the save's own transaction.
/// </para>
/// </remarks>
internal sealed class DeclaredTableApplier(PageRecordOptions records) {

    // The declaration as the catalogue keeps it: readable JSON, so an operator looking at the table
    // can see what was applied without decoding anything.
    private static readonly JsonSerializerOptions s_json = new() {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads the catalogue rows one source declared.</summary>
    /// <param name="database">The context to read through.</param>
    /// <param name="source">The declaring source's key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rows, in the order they were made.</returns>
    public static async Task<IReadOnlyList<DeclaredTable>> ListAsync(
        StorageDbContext database,
        PagePath source,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(source);

        DeclaredTableEntry[] rows = await database.Set<DeclaredTableEntry>().AsNoTracking()
            .Where(row => row.SourceKey == source.Key)
            .OrderBy(row => row.Id)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => new DeclaredTable(row.Name, row.Subject, row.Lifetime, row.Columns, row.RealName, row.Search))];
    }

    /// <summary>Applies one source's declaration to the schema.</summary>
    /// <param name="database">The context whose transaction the DDL belongs to.</param>
    /// <param name="source">The declaring source's key.</param>
    /// <param name="schema">The declaration the source carries.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The problems that stopped it, or an empty list when the schema was applied.</returns>
    /// <remarks>
    /// A problem is a <c>PAGE012</c> and applies nothing at all: not the additions and not the drops, so
    /// a fixed declaration is applied from the same place the broken one left off.
    /// </remarks>
    public async Task<IReadOnlyList<PageDiagnostic>> ApplyAsync(
        StorageDbContext database,
        PagePath source,
        PageSchema schema,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);

        List<PageDiagnostic> problems = [.. schema.Validate(records.SchemaLimits)
            .Select(problem => Problem(Describe(problem)))];

        IReadOnlyList<DeclaredTable> applied = await ListAsync(database, source, cancellationToken);
        problems.AddRange(await CompareAsync(database, applied, schema, cancellationToken));

        if (problems.Count > 0) {
            return problems;
        }

        await ApplyTablesAsync(database, source, applied, schema, cancellationToken);

        return [];
    }

    /// <summary>Removes every row one subject holds in every visitor's declared table.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="subject">The subject string.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many rows were removed.</returns>
    /// <remarks>
    /// A declared table belongs to the source that declared it and holds one subject's rows behind an
    /// owner column, so ending a session, or removing an account, has to walk the catalogue: the
    /// subjects a page may hold are not known to any one table.
    /// </remarks>
    public static async Task<int> RemoveSubjectAsync(
        StorageDbContext database,
        string subject,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        DeclaredTableEntry[] tables = await database.Set<DeclaredTableEntry>().AsNoTracking()
            .Where(row => row.Subject == (int)StoreTableSubject.Visitor)
            .ToArrayAsync(cancellationToken);

        int removed = 0;
        foreach (DeclaredTableEntry table in tables) {
            removed += await database.Database.ExecuteSqlAsync(
                $"""DELETE FROM "{table.RealName}" WHERE "Owner" = {subject}""", cancellationToken);
        }

        return removed;
    }

    /// <summary>Removes every table one source declared, and its catalogue rows.</summary>
    /// <param name="database">The context whose transaction the drops belong to.</param>
    /// <param name="source">The source that declared them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <remarks>A page's delete, a scrub and an account's removal all come through here.</remarks>
    public static async Task DropAsync(StorageDbContext database, PagePath source, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(source);

        IReadOnlyList<DeclaredTable> tables = await ListAsync(database, source, cancellationToken);
        await DropAllAsync(database, source, tables, cancellationToken);
    }

    // The difference between what was applied and what the source now declares, as problems. A type
    // that disagrees is a refusal rather than a rebuild: nothing here reinterprets rows.
    private static async Task<List<PageDiagnostic>> CompareAsync(
        StorageDbContext database,
        IReadOnlyList<DeclaredTable> applied,
        PageSchema schema,
        CancellationToken cancellationToken) {
        List<PageDiagnostic> problems = [];

        foreach (PageTable table in schema.Tables) {
            DeclaredTable? stored = applied.FirstOrDefault(row => string.Equals(row.Name, table.Name, StringComparison.Ordinal));
            if (stored is null) {
                continue;
            }

            if (stored.Subject != (int)table.Subject || stored.Lifetime != (int)table.Lifetime) {
                problems.Add(Problem(
                    $"\"{table.Name}\" was applied as {Describe((StoreTableSubject)stored.Subject)} and {Describe((StoreTableLifetime)stored.Lifetime)}, and the declaration now says {Describe(table.Subject)} and {Describe(table.Lifetime)}; a table keeps the subject and lifetime it was made with, so a change is a new table under a new name."));
                continue;
            }

            List<PageColumn> before = Read(stored.Columns);
            foreach (PageColumn column in table.Columns) {
                PageColumn? was = before.FirstOrDefault(candidate => string.Equals(candidate.Name, column.Name, StringComparison.Ordinal));
                if (was is not null && (was.Type != column.Type || was.Nullable != column.Nullable)) {
                    problems.Add(Problem(
                        $"the column \"{column.Name}\" on \"{table.Name}\" was applied as {Describe(was)} and the declaration now says {Describe(column)}; a column keeps the type it was made with, so the answer is a new column with a new name."));
                }
            }

            problems.AddRange(await CompareLiveAsync(database, stored, table, cancellationToken));
        }

        return problems;
    }

    // What the real table holds now against what the catalogue says was applied. The catalogue is the
    // record, so a disagreement means something changed the table outside the host.
    private static async Task<List<PageDiagnostic>> CompareLiveAsync(
        StorageDbContext database,
        DeclaredTable stored,
        PageTable table,
        CancellationToken cancellationToken) {
        List<PageDiagnostic> problems = [];
        Dictionary<string, string> live = await ColumnsAsync(database, stored.RealName, cancellationToken);

        foreach (PageColumn column in table.Columns) {
            if (live.TryGetValue(column.Name, out string? type)
                && !string.Equals(type, DeclaredTableSql.StorageClass(column.Type), StringComparison.OrdinalIgnoreCase)) {
                problems.Add(Problem(
                    $"the column \"{column.Name}\" on \"{table.Name}\" holds {type} in the database, and the declaration says {DeclaredTableSql.StorageClass(column.Type)}; that table was changed outside the host, so it is not altered here."));
            }
        }

        return problems;
    }

    private async Task ApplyTablesAsync(
        StorageDbContext database,
        PagePath source,
        IReadOnlyList<DeclaredTable> applied,
        PageSchema schema,
        CancellationToken cancellationToken) {
        // A table the source no longer declares goes first, with every row in it: the source is the
        // declaration, and a table it does not name is not half-kept.
        foreach (DeclaredTable gone in applied.Where(row =>
            !schema.Tables.Any(table => string.Equals(table.Name, row.Name, StringComparison.Ordinal)))) {
            await ExecuteAsync(database, DeclaredTableSql.Drop(gone.RealName, gone.Search is not null), cancellationToken);
            await database.Set<DeclaredTableEntry>()
                .Where(row => row.SourceKey == source.Key && row.Name == gone.Name)
                .ExecuteDeleteAsync(cancellationToken);
        }

        List<DeclaredTable> kept = [.. applied.Where(row =>
            schema.Tables.Any(table => string.Equals(table.Name, row.Name, StringComparison.Ordinal)))];

        foreach (PageTable table in schema.Tables) {
            DeclaredTable? stored = kept.FirstOrDefault(row => string.Equals(row.Name, table.Name, StringComparison.Ordinal));
            if (stored is null) {
                await CreateAsync(database, source, table, cancellationToken);
            } else {
                await UpdateAsync(database, source, stored, table, cancellationToken);
            }
        }

        // The catalogue rows are the record of the DDL that has just run, so they are written here
        // rather than left to the caller: a save that forgot them would re-create the tables next time.
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task CreateAsync(StorageDbContext database, PagePath source, PageTable table, CancellationToken cancellationToken) {
        string real = DeclaredTableSql.MintRealName();
        List<string> statements = [DeclaredTableSql.CreateTable(real, table)];

        if (table.Subject is StoreTableSubject.Visitor) {
            statements.Add(DeclaredTableSql.CreateOwnerIndex(real));
        }

        if (table.Search is not null) {
            statements.AddRange(DeclaredTableSql.CreateSearch(real, table));
        }

        statements.AddRange(DeclaredTableSql.CreateCaps(real, table, records));
        await ExecuteAsync(database, statements, cancellationToken);

        database.Add(new DeclaredTableEntry {
            SourceKey = source.Key,
            Name = table.Name,
            Subject = (int)table.Subject,
            Lifetime = (int)table.Lifetime,
            Columns = Write(table.Columns),
            Search = table.Search,
            RealName = real,
        });
    }

    private async Task UpdateAsync(
        StorageDbContext database,
        PagePath source,
        DeclaredTable stored,
        PageTable table,
        CancellationToken cancellationToken) {
        List<PageColumn> before = Read(stored.Columns);
        bool searchMoved = stored.Search is { } was
            && !string.Equals(was, table.Search, StringComparison.Ordinal);

        // The triggers go first and come back last: each names every column, and SQLite re-parses a
        // trigger when the table under it changes, so altering a column while one still stands fails.
        List<string> statements = [.. DeclaredTableSql.DropCaps(stored.RealName)];
        if (searchMoved) {
            statements.AddRange(DeclaredTableSql.DropSearch(stored.RealName));
        }

        foreach (PageColumn gone in before.Where(column =>
            !table.Columns.Any(declared => string.Equals(declared.Name, column.Name, StringComparison.Ordinal)))) {
            statements.Add(DeclaredTableSql.DropColumn(stored.RealName, gone.Name));
        }

        foreach (PageColumn added in table.Columns.Where(column =>
            !before.Any(was => string.Equals(was.Name, column.Name, StringComparison.Ordinal)))) {
            statements.Add(DeclaredTableSql.AddColumn(stored.RealName, added));
        }

        // An index follows the column it matches, so a moved search column rebuilds it.
        if (table.Search is not null && (stored.Search is null || searchMoved)) {
            statements.AddRange(DeclaredTableSql.CreateSearch(stored.RealName, table));
        }

        // Every save rewrites the caps, so a changed one takes effect rather than only bounding the
        // tables made after it.
        statements.AddRange(DeclaredTableSql.CreateCaps(stored.RealName, table, records));
        await ExecuteAsync(database, statements, cancellationToken);

        await database.Set<DeclaredTableEntry>()
            .Where(row => row.SourceKey == source.Key && row.Name == stored.Name)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Columns, Write(table.Columns))
                .SetProperty(row => row.Search, table.Search), cancellationToken);
    }

    private static async Task DropAllAsync(
        StorageDbContext database,
        PagePath source,
        IReadOnlyList<DeclaredTable> tables,
        CancellationToken cancellationToken) {
        foreach (DeclaredTable table in tables) {
            await ExecuteAsync(database, DeclaredTableSql.Drop(table.RealName, table.Search is not null), cancellationToken);
        }

        await database.Set<DeclaredTableEntry>()
            .Where(row => row.SourceKey == source.Key)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> ColumnsAsync(
        StorageDbContext database,
        string realName,
        CancellationToken cancellationToken) {
        Dictionary<string, string> columns = [with(StringComparer.Ordinal)];

        await database.Database.OpenConnectionAsync(cancellationToken);
        DbConnection connection = database.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        // The name is the one this applier minted and stored, so nothing a page wrote reaches it.
#pragma warning disable CA2100 // the real name is a base64url token the host made, never a page's
        command.CommandText = $"PRAGMA table_info(\"{realName}\")";
#pragma warning restore CA2100
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) {
            columns[reader.GetString(1)] = reader.GetString(2);
        }

        return columns;
    }

    private static async Task ExecuteAsync(StorageDbContext database, IReadOnlyList<string> statements, CancellationToken cancellationToken) {
        foreach (string statement in statements) {
            await database.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }
    }

    private static string Write(IReadOnlyList<PageColumn> columns) => JsonSerializer.Serialize(columns, s_json);

    private static List<PageColumn> Read(string json) =>
        [.. (JsonSerializer.Deserialize<PageColumn[]>(json, s_json) ?? [])];

    private static string Describe(PageColumn column) =>
        $"{(column.Nullable ? "a nullable " : "a ")}{Describe(column.Type)}";

    private static string Describe(PageColumnType type) => type switch {
        PageColumnType.Text => "string",
        PageColumnType.WholeNumber => "long",
        PageColumnType.Number => "double",
        PageColumnType.Flag => "bool",
        PageColumnType.Timestamp => "DateTimeOffset",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a declared column type."),
    };

    private static string Describe(StoreTableSubject subject) =>
        subject is StoreTableSubject.Page ? "the page's" : "one visitor's";

    private static string Describe(StoreTableLifetime lifetime) => lifetime switch {
        StoreTableLifetime.Page => "Page",
        StoreTableLifetime.Person => "Person",
        StoreTableLifetime.Session => "Session",
        StoreTableLifetime.Browser => "Browser",
        _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Not a table lifetime."),
    };

    private static string Describe(PageSchemaProblem problem) => problem.Column is { } column
        ? $"\"{problem.Table}\" cannot be applied: the column \"{column}\" {problem.Message}"
        : problem.Table is { } table
            ? $"\"{table}\" cannot be applied: {problem.Message}"
            : $"the declaration cannot be applied: {problem.Message}";

    private static PageDiagnostic Problem(string message) =>
        new(PageDiagnosticSeverity.Error, null, $"PAGE012: {message}");
}
