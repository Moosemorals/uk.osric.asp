// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Pages;

/// <summary>The pages an archive carries, or the reasons it was refused.</summary>
/// <param name="Pages">The validated pages, in key order; empty when <paramref name="Problems"/> is not.</param>
/// <param name="Problems">Every rule the archive broke, up to the reporting limit.</param>
internal sealed record PageArchiveReadResult(IReadOnlyList<PageArchiveEntry> Pages, IReadOnlyList<string> Problems) {

    /// <summary>Whether every row passed every rule.</summary>
    public bool Succeeded => Problems.Count == 0;
}

/// <summary>Reads an uploaded archive as hostile input, and stages nothing unless every rule holds.</summary>
/// <remarks>
/// <para>
/// The file reaches SQLite's parser, which is a real boundary: it is deserialized <em>read-only</em>
/// into a throwaway in-memory connection, <c>query_only</c> and <c>trusted_schema</c> are set before
/// any query, the two tables must be ordinary tables with exactly the expected columns,
/// <c>quick_check</c> runs once, and every row is validated before anything is returned.
/// </para>
/// <para>
/// Validation is all or nothing. Problems are collected — up to ten — only so the error page can
/// show several at once; each one is independently fatal, and nothing is skipped, renamed or repaired.
/// </para>
/// </remarks>
internal sealed class PageArchiveReader(
    ArchiveOptions options,
    PageStorageOptions storage,
    ReservedPaths reserved,
    ILogger<PageArchiveReader> logger) {

    /// <summary>How many problems are reported before the scan stops.</summary>
    public const int MaxProblems = 10;

    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads and validates an archive.</summary>
    /// <param name="archive">The uploaded bytes.</param>
    /// <returns>The candidates, or the problems that rejected them.</returns>
    public PageArchiveReadResult Read(byte[] archive) {
        ArgumentNullException.ThrowIfNull(archive);

        if (archive.Length == 0) {
            return Reject("The file is empty.");
        }

        List<string> problems = [];
        GCHandle pinned = GCHandle.Alloc(archive, GCHandleType.Pinned);

        try {
            using SqliteConnection connection = new("Data Source=:memory:");
            connection.Open();

            try {
                if (SQLitePCL.raw.sqlite3_deserialize(
                        connection.Handle,
                        "main",
                        pinned.AddrOfPinnedObject(),
                        archive.Length,
                        archive.Length,
                        SQLitePCL.raw.SQLITE_DESERIALIZE_READONLY) != SQLitePCL.raw.SQLITE_OK) {
                    return Reject("That file is not a SQLite database.");
                }

                // Read-only and with schema-defined objects untrusted, before anything is queried.
                SqliteStatement.Execute(connection, "PRAGMA query_only = ON");
                SqliteStatement.Execute(connection, "PRAGMA trusted_schema = OFF");

                return Read(connection, problems);
            } catch (SqliteException exception) {
                // The upload is a file somebody else made: refusing it is the outcome, and the
                // engine's complaint is only interesting to a developer reading the journal.
                logger.LogDebug(exception, "An uploaded archive could not be opened as a SQLite database");
                return Reject("That file is not a readable SQLite database.");
            } finally {
                connection.Close();
            }
        } finally {
            pinned.Free();
        }
    }

    private PageArchiveReadResult Read(SqliteConnection connection, List<string> problems) {
        if (!ReadFormat(connection, problems) || !ReadSchema(connection, problems)) {
            return new PageArchiveReadResult([], problems);
        }

        List<PageArchiveEntry> pages = ReadPages(connection, problems);
        if (problems.Count > 0) {
            return new PageArchiveReadResult([], problems);
        }

        ReadState(connection, pages, problems);
        if (problems.Count > 0) {
            return new PageArchiveReadResult([], problems);
        }

        return new PageArchiveReadResult(
            [.. pages.OrderBy(page => page.Path.Key, StringComparer.Ordinal)],
            []);
    }

    // The identity comes before the schema: a database written by something else is not a page
    // archive at all, and saying so is more useful than describing a schema it never had.
    private static bool ReadFormat(SqliteConnection connection, List<string> problems) {
        long applicationId = ReadInteger(connection, "PRAGMA application_id");
        if (applicationId != PageArchiveFormat.ApplicationId) {
            Add(problems, "That file is not a page archive: it does not carry the archive's identity.");
            return false;
        }

        long version = ReadInteger(connection, "PRAGMA user_version");
        if (version != PageArchiveFormat.Version) {
            Add(problems,
                $"That archive was written by another version of the site (format {version.ToString(CultureInfo.InvariantCulture)}); this one reads format {PageArchiveFormat.Version.ToString(CultureInfo.InvariantCulture)}.");
            return false;
        }

        foreach (string line in ReadStrings(connection, "PRAGMA quick_check")) {
            if (!string.Equals(line, "ok", StringComparison.OrdinalIgnoreCase)) {
                Add(problems, "That archive is damaged: its own integrity check did not pass.");
                return false;
            }
        }

        return true;
    }

    private static bool ReadSchema(SqliteConnection connection, List<string> problems) {
        bool ok = true;

        List<(string Type, string Name)> objects = [];
        using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = """SELECT "type", "name" FROM sqlite_master WHERE "name" NOT LIKE 'sqlite\_%' ESCAPE '\'""";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) {
                objects.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        HashSet<(string, string)> expected = [
            ("table", PageArchiveFormat.PagesTable),
            ("table", PageArchiveFormat.PageStateTable),
        ];

        foreach ((string type, string name) in objects) {
            if (!expected.Contains((type, name))) {
                // The name is the archive's own text, so it is quoted the way every other untrusted
                // value is: one line, printable, and bounded.
                string quoted = MessageText.Sanitize(name);
                Add(problems, type switch {
                    "table" => $"That archive has an unexpected table, \"{quoted}\"; a page archive holds exactly \"{PageArchiveFormat.PagesTable}\" and \"{PageArchiveFormat.PageStateTable}\".",
                    "view" => $"That archive has a view, \"{quoted}\"; a page archive holds exactly \"{PageArchiveFormat.PagesTable}\" and \"{PageArchiveFormat.PageStateTable}\".",
                    "trigger" => $"That archive has a trigger, \"{quoted}\"; a page archive holds exactly two tables and nothing else.",
                    "index" => $"That archive has an index, \"{quoted}\"; a page archive holds exactly two tables and nothing else.",
                    _ => $"That archive has an unexpected \"{quoted}\" entry; a page archive holds exactly two tables and nothing else.",
                });
                ok = false;
            }
        }

        foreach ((_, string name) in expected) {
            if (!objects.Any(item => string.Equals(item.Name, name, StringComparison.Ordinal))) {
                Add(problems, $"That archive is missing its \"{name}\" table.");
                ok = false;
            }
        }

        if (!ok) {
            return false;
        }

        return CheckColumns(connection, PageArchiveFormat.PagesTable, PageArchiveFormat.PagesColumns, problems)
            && CheckColumns(connection, PageArchiveFormat.PageStateTable, PageArchiveFormat.PageStateColumns, problems);
    }

    private static bool CheckColumns(
        SqliteConnection connection,
        string table,
        IReadOnlyList<ArchiveColumn> expected,
        List<string> problems) {
        List<ArchiveColumn> actual = [];

        using (SqliteCommand command = connection.CreateCommand()) {
            // The table name is one of the two constants above, never a value from the archive.
            command.CommandText = table switch {
                PageArchiveFormat.PagesTable => """PRAGMA table_info("Pages")""",
                PageArchiveFormat.PageStateTable => """PRAGMA table_info("PageState")""",
                _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not an archive table."),
            };

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) {
                actual.Add(new ArchiveColumn(reader.GetString(1), reader.GetInt64(3) != 0, (int)reader.GetInt64(5)));
            }
        }

        if (actual.Count != expected.Count) {
            Add(problems, $"That archive's \"{table}\" table does not have the columns a page archive uses.");
            return false;
        }

        for (int index = 0; index < expected.Count; index++) {
            ArchiveColumn want = expected[index];
            ArchiveColumn have = actual[index];

            if (!string.Equals(want.Name, have.Name, StringComparison.Ordinal)
                || want.NotNull != have.NotNull
                || want.PrimaryKey != have.PrimaryKey) {
                Add(problems, $"That archive's \"{table}\" table does not have the columns a page archive uses.");
                return false;
            }
        }

        return true;
    }

    private List<PageArchiveEntry> ReadPages(SqliteConnection connection, List<string> problems) {
        (long count, long bytes) = ReadPageTotals(connection);
        if (count == 0) {
            Add(problems, "That archive holds no pages; an export with nothing in it is a mistake, not a no-op.");
            return [];
        }

        if (count > options.MaxPages) {
            Add(problems,
                $"That archive holds more than {options.MaxPages.ToString(CultureInfo.InvariantCulture)} pages, which is this site's import limit.");
            return [];
        }

        // Summed in SQL before a string is read, so a crafted row cannot make the host allocate a
        // value it did not pay for in file bytes.
        if (bytes > options.MaxUploadBytes) {
            Add(problems, "That archive's page source is larger than this site will import.");
            return [];
        }

        List<PageArchiveEntry> pages = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = """
                SELECT typeof("Path"), typeof("Source"), CAST("Path" AS BLOB), CAST("Source" AS BLOB)
                FROM "Pages"
                """;

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) {
                if (problems.Count >= MaxProblems) {
                    return [];
                }

                if (!AreText(reader, 2, problems, PageArchiveFormat.PagesTable)) {
                    continue;
                }

                if (!TryDecode(reader.GetFieldValue<byte[]>(2), out string key, problems, "a page path")
                    || !TryDecode(reader.GetFieldValue<byte[]>(3), out string source, problems, "page source")) {
                    continue;
                }

                PagePath path = PagePath.Normalize(key);
                if (!string.Equals(path.Key, key, StringComparison.Ordinal)) {
                    Add(problems, $"That archive has the page \"{MessageText.Sanitize(key)}\", which is not its canonical key; a page archive names each page exactly as this site would.");
                    continue;
                }

                if (!path.IsAddressable) {
                    Add(problems, $"That archive has the page \"{MessageText.Sanitize(key)}\", which no URL can name.");
                    continue;
                }

                if (reserved.IsReserved(path)) {
                    Add(problems, $"That archive has the page \"{MessageText.Sanitize(key)}\", which is reserved: the site's own routes answer for it, so it cannot be imported.");
                    continue;
                }

                if (!seen.Add(path.Key)) {
                    Add(problems, $"That archive names the page \"{MessageText.Sanitize(key)}\" more than once.");
                    continue;
                }

                pages.Add(new PageArchiveEntry(path, source, []));
            }
        }

        return pages;
    }

    private void ReadState(SqliteConnection connection, List<PageArchiveEntry> pages, List<string> problems) {
        Dictionary<string, PageArchiveEntry> byKey = pages.ToDictionary(page => page.Path.Key, StringComparer.Ordinal);
        Dictionary<string, List<PageStateValue>> state = new(StringComparer.Ordinal);
        Dictionary<string, long> byteTotals = new(StringComparer.Ordinal);

        using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = """
                SELECT typeof("PagePath"), typeof("Key"), typeof("Value"),
                       CAST("PagePath" AS BLOB), CAST("Key" AS BLOB), CAST("Value" AS BLOB)
                FROM "PageState"
                """;

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) {
                if (problems.Count >= MaxProblems) {
                    return;
                }

                if (!AreText(reader, 3, problems, PageArchiveFormat.PageStateTable)) {
                    continue;
                }

                if (!TryDecode(reader.GetFieldValue<byte[]>(3), out string pagePath, problems, "a state page path")
                    || !TryDecode(reader.GetFieldValue<byte[]>(4), out string key, problems, "a state key")
                    || !TryDecode(reader.GetFieldValue<byte[]>(5), out string value, problems, "a state value")) {
                    continue;
                }

                if (!byKey.ContainsKey(pagePath)) {
                    Add(problems, $"That archive has state for \"{MessageText.Sanitize(pagePath)}\", which is not a page it carries.");
                    continue;
                }

                if (!PageStateKey.IsValid(key, storage.MaxKeyChars)) {
                    Add(problems, $"That archive has the state key \"{MessageText.Sanitize(key)}\" for \"{MessageText.Sanitize(pagePath)}\", which is not a key this site stores (1–{storage.MaxKeyChars.ToString(CultureInfo.InvariantCulture)} characters, no control characters).");
                    continue;
                }

                int valueBytes = Encoding.UTF8.GetByteCount(value);
                if (valueBytes > storage.MaxValueBytes) {
                    Add(problems, $"That archive's state value for \"{MessageText.Sanitize(key)}\" on \"{MessageText.Sanitize(pagePath)}\" is larger than this site stores.");
                    continue;
                }

                if (!IsJson(value)) {
                    Add(problems, $"That archive's state value for \"{MessageText.Sanitize(key)}\" on \"{MessageText.Sanitize(pagePath)}\" is not JSON.");
                    continue;
                }

                List<PageStateValue> values = state.TryGetValue(pagePath, out List<PageStateValue>? list)
                    ? list
                    : state[pagePath] = [];
                values.Add(new PageStateValue(key, value));
                byteTotals[pagePath] = byteTotals.GetValueOrDefault(pagePath) + Encoding.UTF8.GetByteCount(key) + valueBytes;
            }
        }

        foreach ((string pagePath, List<PageStateValue> values) in state) {
            if (values.Count > storage.MaxKeysPerPage) {
                Add(problems, $"That archive holds more state keys for \"{MessageText.Sanitize(pagePath)}\" than this site stores for one page.");
                continue;
            }

            if (byteTotals[pagePath] > storage.MaxPageBytes) {
                Add(problems, $"That archive holds more state for \"{MessageText.Sanitize(pagePath)}\" than this site stores for one page.");
            }
        }

        if (problems.Count > 0) {
            return;
        }

        for (int index = 0; index < pages.Count; index++) {
            if (state.TryGetValue(pages[index].Path.Key, out List<PageStateValue>? values)) {
                values.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
                pages[index] = pages[index] with { State = values };
            }
        }
    }

    private static (long Count, long Bytes) ReadPageTotals(SqliteConnection connection) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(length(CAST("Path" AS BLOB)) + length(CAST("Source" AS BLOB))), 0)
            FROM "Pages"
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetInt64(1)) : (0, 0);
    }

    // A TEXT column holds text; anything else was crafted, and reading it as a string would quietly
    // turn a blob or a NULL into page content.
    private static bool AreText(SqliteDataReader reader, int count, List<string> problems, string table) {
        for (int index = 0; index < count; index++) {
            if (!string.Equals(reader.GetString(index), "text", StringComparison.Ordinal)) {
                Add(problems, $"That archive has a non-text value in \"{table}\"; a page archive stores text only.");
                return false;
            }
        }

        return true;
    }

    private bool TryDecode(byte[] bytes, out string text, List<string> problems, string what) {
        try {
            text = s_strictUtf8.GetString(bytes);
            return true;
        } catch (DecoderFallbackException exception) {
            // Already reported as a problem on the review page; the decoder's own account of which
            // byte it choked on is only useful to whoever is debugging the reader.
            logger.LogDebug(exception, "An uploaded archive carries {What} that is not valid UTF-8", what);
            text = string.Empty;
            Add(problems, $"That archive has {what} that is not valid UTF-8.");
            return false;
        }
    }

    // The page's own reads expect JSON, and the store never writes anything else.
    private bool IsJson(string value) {
        try {
            using JsonDocument document = JsonDocument.Parse(value);
            return true;
        } catch (JsonException exception) {
            logger.LogDebug(exception, "An uploaded archive carries state that is not JSON");
            return false;
        }
    }

    private static void Add(List<string> problems, string problem) {
        if (problems.Count < MaxProblems) {
            problems.Add(problem);
        }
    }

    private static List<string> ReadStrings(SqliteConnection connection, string sql) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<string> values = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) {
            if (!reader.IsDBNull(0)) {
                values.Add(reader.GetValue(0)?.ToString() ?? string.Empty);
            }
        }

        return values;
    }

    private static long ReadInteger(SqliteConnection connection, string sql) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static PageArchiveReadResult Reject(string problem) => new([], [problem]);
}
