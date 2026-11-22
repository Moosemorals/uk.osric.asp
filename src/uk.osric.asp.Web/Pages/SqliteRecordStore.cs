// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Data.Sqlite;
using System.Collections.Frozen;
using System.Data;
using System.Globalization;
using System.Text.Json;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Sdk;

/// <summary>One subject's declared tables, behind the fence slice 0 measured.</summary>
/// <remarks>
/// <para>
/// The sandbox is a fence for one statement, not a claim of safety. Each declared table exists once in
/// <c>main</c> under a name nobody can guess; this store makes, in <c>temp</c>, one view per table with
/// the subject as a literal in its predicate, and an <c>INSTEAD OF</c> trigger per verb writing the
/// same literal back. A page's SQL runs against the views, so a join or an aggregate sees only its own
/// rows and the real table cannot be named.
/// </para>
/// <para>
/// The views are the whole allow-list and SQLite enforces it: <c>sqlite3_set_authorizer</c> allows
/// SELECT and RECURSIVE, a read or write of the handle's own view names, a read of a real table only
/// when the innermost view or trigger is one of them, a read with no database (a common table
/// expression, which is not a table) and a closed list of functions. Everything else is denied by
/// omission, by SQLite's own parser rather than by a scanner.
/// </para>
/// </remarks>
internal sealed class SqliteRecordStore : RenderBoundCapability, IRecordStore, IAsyncDisposable {

    // The functions a page may call. A closed list rather than a deny-list: what is not here is
    // refused, so a function a later SQLite adds is refused until someone decides it may run.
    private static readonly FrozenSet<string> s_functions = FrozenSet.ToFrozenSet([
        "abs", "avg", "char", "coalesce", "count", "date", "datetime", "format", "glob", "group_concat",
        "hex", "ifnull", "iif", "instr", "julianday", "length", "like", "lower", "ltrim", "max", "min",
        "nullif", "printf", "quote", "random", "replace", "round", "rtrim", "sign", "soundex", "strftime",
        "substr", "substring", "sum", "time", "total", "trim", "typeof", "unicode", "unichar", "unlikely",
        "upper",
    ], StringComparer.OrdinalIgnoreCase);

    private readonly PagePath _source;
    private readonly Func<StoreTableLifetime, string>? _subject;
    private readonly SubjectKind _kind;
    private readonly IRecordCatalog _catalog;
    private readonly PageRecordOptions _records;
    private readonly bool _writable;

    private SqliteConnection? _connection;
    private FrozenDictionary<string, PageTable> _tables = [];
    private FrozenDictionary<string, string> _subjects = [];
    private FrozenSet<string> _views = [];
    private FrozenSet<string> _inner = [];
    private FrozenSet<string> _shadow = [];
    private string? _refused;
    private SqliteTransaction? _transaction;
    private bool _host;
    private bool _disposed;

    /// <summary>Creates a handle over one source's tables.</summary>
    /// <param name="source">The key of the source that declared them.</param>
    /// <param name="subject">
    /// The subject a visitor's rows carry, by the table's lifetime, or <see langword="null"/> for the
    /// page's own tables.
    /// </param>
    /// <param name="kind">Whose tables this handle reaches.</param>
    /// <param name="catalog">Where the catalogue and the database are read.</param>
    /// <param name="records">The caps and the clocks.</param>
    /// <param name="writable">Whether this render may write.</param>
    public SqliteRecordStore(
        PagePath source,
        Func<StoreTableLifetime, string>? subject,
        SubjectKind kind,
        IRecordCatalog catalog,
        PageRecordOptions records,
        bool writable) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(records);

        _source = source;
        _subject = subject;
        _kind = kind;
        _catalog = catalog;
        _records = records;
        _writable = writable;
    }

    /// <summary>Whose rows a handle reaches.</summary>
    internal enum SubjectKind {
        /// <summary>The page's own tables: shared by every visitor, with no owner column.</summary>
        Page,

        /// <summary>One visitor's tables, filtered by the lifetime's subject.</summary>
        Visitor,
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageRow>> QueryAsync(string sql, PageParameters? parameters = null) {
        ThrowIfReadOnly(write: false);

        return await ReadAsync(sql, parameters, host: false);
    }

    /// <inheritdoc/>
    public Task<PageRecordResult> ExecuteAsync(string sql, PageParameters? parameters = null) {
        ThrowIfReadOnly(write: true);

        return WriteAsync(sql, parameters);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageRow>> SearchAsync(string table, string term, int? limit = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(term);
        ThrowIfReadOnly(write: false);

        await OpenAsync(CancellationToken.None);
        if (!_tables.TryGetValue(table, out PageTable? declared) || declared.Search is null) {
            throw new ArgumentException(
                $"\"{table}\" is not a table this page declares with a search column, so there is nothing to search.",
                nameof(table));
        }

        DeclaredTable stored = await StoredAsync(declared.Name, CancellationToken.None);
        int cap = Math.Clamp(limit ?? _records.MaxResultRows, 1, _records.MaxResultRows);
        string? carried = _subjects.GetValueOrDefault(declared.Name);
        string owner = carried is null ? string.Empty : " AND t.\"Owner\" = $owner";
        string index = DeclaredTableSql.SearchName(stored.RealName);

        // The host's statement, because the index holds every subject's rows: it runs with the
        // widening flag and filters the subject itself, before a row is answered.
        return await ReadAsync($"""
            SELECT t.* FROM "{index}"
            JOIN "{stored.RealName}" AS t ON t."Id" = "{index}".rowid
            WHERE "{index}" MATCH $term{owner}
            ORDER BY rank
            LIMIT {cap}
            """, new PageParameters().Add("term", term).Add("owner", carried), host: true);
    }

    /// <inheritdoc/>
    public IPageRecordTransaction BeginTransaction() {
        ThrowIfReadOnly(write: true);
        if (_transaction is not null) {
            throw new InvalidOperationException(
                $"{_source.Key} already has a transaction open; a handle holds one at a time, and a second is a page that forgot to commit.");
        }

        OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
        using (Host()) {
            _transaction = _connection!.BeginTransaction();
        }

        return new Frame(this);
    }

    /// <summary>Ends the handle, closing its connection and every view with it.</summary>
    /// <returns>A task that completes when the connection is closed.</returns>
    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        await EndAsync();

        if (_connection is not null) {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    /// <inheritdoc/>
    public override void Abandon() {
        base.Abandon();
        // The interrupt is what ends a statement Microsoft.Data.Sqlite will not end for us: its own
        // timeout is a busy timeout, and a cancellation token alone leaves a running statement alone.
        Interrupt();
    }

    internal async Task CommitAsync() {
        // The transaction object owns the COMMIT: issuing it as text would leave the object believing
        // it is still open, and disposing it would then try to roll back nothing.
        using (Host()) {
            if (_transaction is not null) {
                await _transaction.CommitAsync();
            }
        }

        await EndAsync();
    }

    internal async Task RollbackAsync() {
        using (Host()) {
            if (_transaction is not null && _connection is { State: ConnectionState.Open }) {
                await _transaction.RollbackAsync();
            }
        }

        await EndAsync();
    }

    private async Task EndAsync() {
        if (_transaction is not null) {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    // Opening is where the whole fence is built: one unpooled connection, the pragmas, the views and
    // their triggers, and the authorizer, in that order.
    private async Task OpenAsync(CancellationToken cancellationToken) {
        if (_connection is not null) {
            return;
        }

        string dataSource = new SqliteConnectionStringBuilder(_catalog.ConnectionString).DataSource;
        SqliteConnection connection = new(new SqliteConnectionStringBuilder {
            DataSource = dataSource,
            Pooling = false,
        }.ToString());

        await connection.OpenAsync(cancellationToken);
        _connection = connection;

        await ConfigureAsync(cancellationToken);
        await BuildAsync(cancellationToken);
        Install(connection);
    }

    private async Task ConfigureAsync(CancellationToken cancellationToken) {
        _ = await HostAsync("PRAGMA trusted_schema = OFF", cancellationToken);
        _ = await HostAsync(
            string.Create(CultureInfo.InvariantCulture, $"PRAGMA busy_timeout = {_records.StatementMaxSeconds * 1000}"),
            cancellationToken);

        SQLitePCL.sqlite3? handle = _connection!.Handle;
        _ = SQLitePCL.raw.sqlite3_db_config(handle, SQLitePCL.raw.SQLITE_DBCONFIG_DEFENSIVE, 1, out _);
        _ = SQLitePCL.raw.sqlite3_db_config(handle, SQLitePCL.raw.SQLITE_DBCONFIG_TRUSTED_SCHEMA, 0, out _);
        _ = SQLitePCL.raw.sqlite3_db_config(handle, SQLitePCL.raw.SQLITE_DBCONFIG_ENABLE_LOAD_EXTENSION, 0, out _);
        _ = SQLitePCL.raw.sqlite3_limit(handle, SQLitePCL.raw.SQLITE_LIMIT_ATTACHED, 0);
        _ = SQLitePCL.raw.sqlite3_limit(handle, SQLitePCL.raw.SQLITE_LIMIT_SQL_LENGTH, 100_000);
    }

    private async Task BuildAsync(CancellationToken cancellationToken) {
        IReadOnlyList<DeclaredTable> catalogued = await _catalog.ListAsync(_source, cancellationToken);

        Dictionary<string, PageTable> tables = [with(StringComparer.Ordinal)];
        Dictionary<string, string> subjects = [with(StringComparer.Ordinal)];
        HashSet<string> views = [with(StringComparer.Ordinal)];
        HashSet<string> inner = [with(StringComparer.Ordinal)];
        HashSet<string> shadow = [with(StringComparer.Ordinal)];

        foreach (DeclaredTable stored in catalogued) {
            if ((SubjectKind)stored.Subject != _kind) {
                continue;
            }

            PageTable declared = Read(stored);
            tables[declared.Name] = declared;

            // The subject is the literal every view and trigger of this table carries.
            string? carried = _subject?.Invoke(declared.Lifetime);
            if (carried is not null) {
                subjects[declared.Name] = carried;
            }

            views.Add(declared.Name);
            // Every trigger the table has, not only the three that write: the cap triggers read the
            // real table and the index triggers write it, and a list of the three would refuse both.
            inner.Add(declared.Name);
            inner.Add(declared.Name + "_insert");
            inner.Add(declared.Name + "_update");
            inner.Add(declared.Name + "_delete");
            inner.Add(stored.RealName + "_cap_insert");
            inner.Add(stored.RealName + "_cap_update");
            inner.Add(stored.RealName + "_ai");
            inner.Add(stored.RealName + "_ad");
            inner.Add(stored.RealName + "_au");

            // FTS5 keeps five tables beside the index and reads them inside its own constructor, so
            // they are allowed by name: the index and the real table are not, or a page could read
            // every subject's text by guessing one.
            if (stored.Search is not null) {
                string index = DeclaredTableSql.SearchName(stored.RealName);
                shadow.Add(index + "_data");
                shadow.Add(index + "_idx");
                shadow.Add(index + "_content");
                shadow.Add(index + "_docsize");
                shadow.Add(index + "_config");
            }

            _ = await HostAsync(View(stored, declared, carried), cancellationToken);
            foreach (string trigger in Triggers(stored, declared, carried)) {
                _ = await HostAsync(trigger, cancellationToken);
            }
        }

        _tables = tables.ToFrozenDictionary(StringComparer.Ordinal);
        _subjects = subjects.ToFrozenDictionary(StringComparer.Ordinal);
        _views = views.ToFrozenSet(StringComparer.Ordinal);
        _inner = inner.ToFrozenSet(StringComparer.Ordinal);
        _shadow = shadow.ToFrozenSet(StringComparer.Ordinal);
    }

    // The view and its triggers are the owner filter: the subject is a literal, never a row the page
    // could update and then read another subject's rows through.
    private static string View(DeclaredTable stored, PageTable table, string? subject) {
        string columns = string.Join(", ", table.Columns.Select(column => $"\"{column.Name}\""));
        string predicate = subject is null ? string.Empty : $" WHERE \"Owner\" = {Literal(subject)}";

        return $"CREATE TEMP VIEW \"{table.Name}\" AS SELECT \"Id\", {columns} FROM \"{stored.RealName}\"{predicate}";
    }

    private static IEnumerable<string> Triggers(DeclaredTable stored, PageTable table, string? subject) {
        string columns = string.Join(", ", table.Columns.Select(column => $"\"{column.Name}\""));
        string inserted = string.Join(", ", table.Columns.Select(column => $"NEW.\"{column.Name}\""));
        string assignments = string.Join(", ", table.Columns.Select(column => $"\"{column.Name}\" = NEW.\"{column.Name}\""));
        string owner = subject is null ? string.Empty : "\"Owner\", ";
        string ownerValue = subject is null ? string.Empty : $"{Literal(subject)}, ";
        string owned = subject is null ? string.Empty : $" AND \"Owner\" = {Literal(subject)}";
        string ownerMove = subject is null ? string.Empty : $", \"Owner\" = {Literal(subject)}";

        // A page may not name a row's id: on a visitor's table that would let it collide with another
        // subject's row and learn from the conflict whether that row exists.
        const string guard =
            "SELECT CASE WHEN NEW.\"Id\" IS NOT NULL THEN RAISE(ABORT, 'a row''s Id is the store''s, so a statement may not name it') END;";

        return [
            $"""
            CREATE TEMP TRIGGER "{table.Name}_insert" INSTEAD OF INSERT ON "{table.Name}" BEGIN
                {guard}
                INSERT INTO "{stored.RealName}" ({owner}{columns}) VALUES ({ownerValue}{inserted});
            END
            """,
            $"""
            CREATE TEMP TRIGGER "{table.Name}_update" INSTEAD OF UPDATE ON "{table.Name}" BEGIN
                UPDATE "{stored.RealName}" SET {assignments}{ownerMove} WHERE "Id" = NEW."Id"{owned};
            END
            """,
            $"""
            CREATE TEMP TRIGGER "{table.Name}_delete" INSTEAD OF DELETE ON "{table.Name}" BEGIN
                DELETE FROM "{stored.RealName}" WHERE "Id" = OLD."Id"{owned};
            END
            """,
        ];
    }

    // The authorizer is installed last, so the fence's own statements never meet it. The one thing
    // that widens it is a flag in this store's own memory, which no statement a page writes can set.
    private void Install(SqliteConnection connection) {
        SQLitePCL.sqlite3? handle = connection.Handle;
        _ = SQLitePCL.raw.sqlite3_set_authorizer(handle, (user, action, name, argument, database, innerMost) => {
            SqliteRecordStore store = (SqliteRecordStore)user;

            if (store._host || store.Allowed(action, name, argument, database, innerMost)) {
                return SQLitePCL.raw.SQLITE_OK;
            }

            store._refused = $"SQLite refused {Describe(action)} on \"{name ?? argument ?? "?"}\"";
            return SQLitePCL.raw.SQLITE_DENY;
        }, this);
    }

    private bool Allowed(int action, string? name, string? argument, string? database, string? innerMost) => action switch {
        SQLitePCL.raw.SQLITE_SELECT or SQLitePCL.raw.SQLITE_RECURSIVE => true,
        // A common table expression is read with no database, and shadows any table of the same name,
        // so allowing it leaks nothing - and refusing it refuses every WITH statement.
        SQLitePCL.raw.SQLITE_READ when database is null => true,
        SQLitePCL.raw.SQLITE_READ or SQLitePCL.raw.SQLITE_INSERT or SQLitePCL.raw.SQLITE_UPDATE
            or SQLitePCL.raw.SQLITE_DELETE => Owned(name, database, innerMost),
        // The function's name arrives in the second argument, not the first: the first is the
        // statement's own text, which is null here.
        SQLitePCL.raw.SQLITE_FUNCTION => argument is not null && s_functions.Contains(argument),
        // The frame's own statements: Microsoft.Data.Sqlite brackets a command inside an open
        // transaction with a savepoint, and a page cannot begin one of its own until the store has.
        SQLitePCL.raw.SQLITE_SAVEPOINT or SQLitePCL.raw.SQLITE_TRANSACTION => _transaction is not null,
        // SQLite asks the connection for this one pragma after a trigger has written, as its own
        // consistency check. It is a counter of commits, not a schema and not a row, so allowing the
        // one name leaks nothing; every other pragma is still refused by omission.
        SQLitePCL.raw.SQLITE_PRAGMA => string.Equals(name, "data_version", StringComparison.Ordinal),
        _ => false,
    };

    private bool Owned(string? name, string? database, string? innerMost) {
        if (name is null) {
            return false;
        }

        if (string.Equals(database, "temp", StringComparison.Ordinal)) {
            return _views.Contains(name);
        }

        return string.Equals(database, "main", StringComparison.Ordinal)
            && ((innerMost is not null && _inner.Contains(innerMost)) || _shadow.Contains(name));
    }

    // The refusal says which rule was hit rather than only that something was: the action and the
    // name SQLite's own parser reported.
    private string Refusal() => _refused is null
        ? "it is not a statement this page may write"
        : _refused;

    private async Task<IReadOnlyList<PageRow>> ReadAsync(string sql, PageParameters? parameters, bool host) {
        ThrowIfEnded();
        await OpenAsync(CancellationToken.None);
        if (!host) {
            CheckOneStatement(sql);
        }

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(Deadline);
        deadline.CancelAfter(_records.StatementTimeout);
        using CancellationTokenRegistration interrupt = deadline.Token.Register(Interrupt);

        bool was = _host;
        _host = host;
        try {
            await using SqliteCommand command = _connection!.CreateCommand();
            // The page's own statement, which the authorizer and the one-statement rule have already
            // judged: quoting it would be judging a copy.
#pragma warning disable CA2100 // the statement is the page's, and the sandbox is what polices it
            command.CommandText = sql;
#pragma warning restore CA2100
            command.CommandTimeout = _records.StatementMaxSeconds;
            if (_transaction is not null) {
                command.Transaction = _transaction;
            }

            Bind(command, parameters);

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(deadline.Token);
            string[] names = [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)];

            List<PageRow> rows = [];
            long chars = 0;
            while (await reader.ReadAsync(deadline.Token)) {
                object?[] values = new object?[names.Length];
                for (int index = 0; index < names.Length; index++) {
                    values[index] = await reader.IsDBNullAsync(index, deadline.Token) ? null : Read(reader, index);
                    chars += values[index] is string text ? text.Length : 0;
                }

                rows.Add(new PageRow(names, values));

                if (rows.Count > _records.MaxResultRows) {
                    throw new InvalidOperationException(
                        $"This statement answered more than Pages:Stores:Records:MaxResultRows ({_records.MaxResultRows}) rows; bound it with LIMIT.");
                }

                if (chars > _records.MaxResultChars) {
                    throw new InvalidOperationException(
                        $"This statement answered more than Pages:Stores:Records:MaxResultChars ({_records.MaxResultChars}) characters; select fewer columns or bound it with LIMIT.");
                }
            }

            return rows;
        } catch (SqliteException exception) when (exception.SqliteErrorCode == 9) {
            throw new TimeoutException(
                $"This statement ran longer than Pages:Stores:Records:StatementMaxSeconds ({_records.StatementMaxSeconds}) and was interrupted.",
                exception);
        } finally {
            _host = was;
            await CloseUnlessOpenAsync();
        }
    }

    private async Task<PageRecordResult> WriteAsync(string sql, PageParameters? parameters) {
        ThrowIfEnded();
        await OpenAsync(CancellationToken.None);
        CheckOneStatement(sql);

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(Deadline);
        deadline.CancelAfter(_records.StatementTimeout);
        using CancellationTokenRegistration interrupt = deadline.Token.Register(Interrupt);

        try {
            await using SqliteCommand command = _connection!.CreateCommand();
#pragma warning disable CA2100 // the statement is the page's, and the sandbox is what polices it
            command.CommandText = sql;
#pragma warning restore CA2100
            command.CommandTimeout = _records.StatementMaxSeconds;
            if (_transaction is not null) {
                command.Transaction = _transaction;
            }

            Bind(command, parameters);
            int affected = await command.ExecuteNonQueryAsync(deadline.Token);

            return new PageRecordResult(affected, _transaction is null ? PageRecordOutcome.Applied : PageRecordOutcome.Staged);
        } catch (SqliteException exception) when (exception.SqliteErrorCode == 5) {
            return new PageRecordResult(0, PageRecordOutcome.Busy);
        } catch (SqliteException exception) when (exception.Message.Contains("FULL", StringComparison.Ordinal)) {
            return new PageRecordResult(0, PageRecordOutcome.Full);
        } catch (SqliteException exception) when (exception.SqliteErrorCode == 9) {
            throw new TimeoutException(
                $"This statement ran longer than Pages:Stores:Records:StatementMaxSeconds ({_records.StatementMaxSeconds}) and was interrupted.",
                exception);
        } finally {
            await CloseUnlessOpenAsync();
        }
    }

    // Exactly one statement, detected by SQLite's own parser: prepare the text, take the tail, prepare
    // the tail and refuse when a second statement resolves. A trailing semicolon, whitespace and a
    // comment are one statement; `SELECT 1; DROP TABLE "t"` is two.
#pragma warning disable CA2000 // a prepared statement is owned by SQLite until it is finalized, which is the next line
    private void CheckOneStatement(string sql) {
        _refused = null;
        int prepared = SQLitePCL.raw.sqlite3_prepare_v2(_connection!.Handle, sql, out SQLitePCL.sqlite3_stmt first, out string tail);
        if (prepared != 0) {
            throw new InvalidOperationException(
                $"This statement was refused: {Refusal()} ({SQLitePCL.raw.sqlite3_errmsg(_connection.Handle).utf8_to_string()}).");
        }

        _ = SQLitePCL.raw.sqlite3_finalize(first);

        // What is left after the first statement is either nothing, a comment, or another statement.
        // Only the last of those is refused; deciding which is a scan for the two things SQLite's
        // parser treats as nothing at all, never a decision about what the statement may touch.
        bool found = HasStatement(tail);

        if (found) {
            throw new ArgumentException(
                "A statement call takes exactly one statement: the text after the first one's semicolon is a second statement, which is refused.",
                nameof(sql));
        }
    }
#pragma warning restore CA2000

    // Whether any text after the first statement is a statement: whitespace, semicolons and comments
    // are not, and the authority for what a statement may then touch is still the authorizer.
    private static bool HasStatement(string text) {
        int index = 0;
        while (index < text.Length) {
            if (char.IsWhiteSpace(text[index]) || text[index] == ';') {
                index++;
            } else if (text.AsSpan(index).StartsWith("--", StringComparison.Ordinal)) {
                int end = text.IndexOf('\n', index);
                index = end < 0 ? text.Length : end + 1;
            } else if (text.AsSpan(index).StartsWith("/*", StringComparison.Ordinal)) {
                int end = text.IndexOf("*/", index, StringComparison.Ordinal);
                index = end < 0 ? text.Length : end + 2;
            } else {
                return true;
            }
        }

        return false;
    }

    private static string Describe(int action) => action switch {
        SQLitePCL.raw.SQLITE_READ => "a read",
        SQLitePCL.raw.SQLITE_INSERT => "an insert",
        SQLitePCL.raw.SQLITE_UPDATE => "an update",
        SQLitePCL.raw.SQLITE_DELETE => "a delete",
        SQLitePCL.raw.SQLITE_FUNCTION => "a function",
        SQLitePCL.raw.SQLITE_PRAGMA => "a pragma",
        SQLitePCL.raw.SQLITE_ATTACH => "an attach",
        SQLitePCL.raw.SQLITE_TRANSACTION => "a transaction",
        SQLitePCL.raw.SQLITE_SAVEPOINT => "a savepoint",
        SQLitePCL.raw.SQLITE_SELECT => "a select",
        _ => $"action {action}",
    };

    private static void Bind(SqliteCommand command, PageParameters? parameters) {
        if (parameters is null) {
            return;
        }

        foreach (KeyValuePair<string, object?> parameter in parameters.Values) {
            command.Parameters.AddWithValue("$" + parameter.Key, parameter.Value switch {
                null => DBNull.Value,
                bool flag => flag ? 1L : 0L,
                DateTimeOffset moment => StoreSubjects.Stamp(moment),
                var value => value,
            });
        }
    }

    private static object Read(SqliteDataReader reader, int index) => reader.GetDataTypeName(index) switch {
        "TEXT" => reader.GetString(index),
        "INTEGER" => reader.GetInt64(index),
        "REAL" => reader.GetDouble(index),
        "BLOB" => reader.GetFieldValue<byte[]>(index),
        var other => throw new InvalidOperationException($"A column of {other} cannot be read into a page row."),
    };

    private async Task<DeclaredTable> StoredAsync(string name, CancellationToken cancellationToken) {
        IReadOnlyList<DeclaredTable> catalogued = await _catalog.ListAsync(_source, cancellationToken);

        return catalogued.FirstOrDefault(row => string.Equals(row.Name, name, StringComparison.Ordinal))
            ?? throw new ArgumentException($"\"{name}\" is not a table this page declares.", nameof(name));
    }

    private static PageTable Read(DeclaredTable stored) => new(
        stored.Name,
        (StoreTableSubject)stored.Subject,
        (StoreTableLifetime)stored.Lifetime,
        [.. JsonSerializer.Deserialize<PageColumn[]>(stored.Columns, JsonSerialization.Options) ?? []],
        stored.Search);

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    // The connection is one call's, or one transaction's: its temp views and its authorizer are per
    // connection, so holding it open between calls would mean holding a handle open for a render that
    // may never use it again.
    private async Task CloseUnlessOpenAsync() {
        if (_transaction is not null || _connection is null) {
            return;
        }

        SqliteConnection connection = _connection;
        _connection = null;
        _tables = [];
        _subjects = [];
        _views = [];
        _inner = [];

        await connection.DisposeAsync();
    }

    private void Interrupt() {
        if (_connection is { State: ConnectionState.Open } connection) {
            SQLitePCL.raw.sqlite3_interrupt(connection.Handle);
        }
    }

    // The host's own statements - the pragmas, the views and their triggers, the frame - run with the
    // authorizer's one widening flag; nothing a page writes can reach this.
    private async Task<int> HostAsync(string sql, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        bool was = _host;
        _host = true;
        try {
            await using SqliteCommand command = _connection!.CreateCommand();
            // Every host statement is written here, from the catalogue's name the host minted.
#pragma warning disable CA2100 // the text is the host's own, never a page's
            command.CommandText = sql;
#pragma warning restore CA2100
            command.CommandTimeout = _records.StatementMaxSeconds;

            return await command.ExecuteNonQueryAsync(cancellationToken);
        } finally {
            _host = was;
        }
    }

    private IDisposable Host() => new HostScope(this);

    private void ThrowIfReadOnly(bool write) {
        if (write && !_writable) {
            throw new InvalidOperationException($"{_source.Key} may not write its records during this render.");
        }
    }

    private void ThrowIfEnded() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsAbandoned) {
            throw new InvalidOperationException($"{_source.Key} cannot reach its records: this render was given up on.");
        }

        if (Deadline.IsCancellationRequested) {
            throw new InvalidOperationException($"{_source.Key} cannot reach its records: this render is past its deadline.");
        }
    }

    /// <summary>The frame a page's statements run inside: a commit, a rollback, and nothing else.</summary>
    private sealed class Frame(SqliteRecordStore store) : IPageRecordTransaction {

        private bool _ended;

        public async Task CommitAsync() {
            ThrowIfEnded();
            _ended = true;
            await store.CommitAsync();
        }

        public async Task RollbackAsync() {
            ThrowIfEnded();
            _ended = true;
            await store.RollbackAsync();
        }

        public async ValueTask DisposeAsync() {
            if (_ended) {
                return;
            }

            _ended = true;
            await store.RollbackAsync();
        }

        private void ThrowIfEnded() {
            if (_ended) {
                throw new InvalidOperationException(
                    "This transaction has already ended; a page that wants another asks the store for one.");
            }
        }
    }

    /// <summary>The synchronous span a host statement runs in, which is only ever this store's own.</summary>
    private sealed class HostScope : IDisposable {

        private readonly SqliteRecordStore _store;
        private readonly bool _was;

        public HostScope(SqliteRecordStore store) {
            _store = store;
            _was = store._host;
            store._host = true;
        }

        public void Dispose() => _store._host = _was;
    }
}
