// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using System.Data;
using System.Data.Common;
using System.Globalization;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core and raw-SQL implementation of <see cref="IStoreObjects"/>.</summary>
/// <remarks>
/// <para>
/// A read does not load the object. The row is found by its digest, then SQLite's incremental blob
/// API opens the <c>Content</c> column by row id, and the reader pulls it in chunks. That is what
/// keeps a large object bounded to the buffer a page or the route chooses rather than to the object.
/// </para>
/// <para>
/// Every read owns its connection and context, and the reader hands them back when it is disposed,
/// because a render can outlive the request scope and an abandoned render must not touch a disposed
/// context.
/// </para>
/// </remarks>
internal sealed class SqliteStoreObjects(
    IDbContextFactory<StorageDbContext> contexts,
    PageStoresOptions options,
    TimeProvider clock,
    PageStorageRefresh? refresh = null) : IStoreObjects {

    /// <inheritdoc/>
    public async Task<PageObjectStream?> OpenPageAsync(string sha, CancellationToken cancellationToken = default) =>
        await OpenCoreAsync([], sha, includePage: true, includeReader: false, cancellationToken);

    /// <inheritdoc/>
    public async Task<PageObjectStream?> OpenAsync(
        IReadOnlyList<string> subjects,
        string sha,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subjects);

        return await OpenCoreAsync(subjects, sha, includePage: false, includeReader: true, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IServedObject?> OpenServedAsync(
        string sha,
        IReadOnlyList<string> subjects,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subjects);

        PageObjectStream? reader = await OpenCoreAsync(
            subjects, sha, includePage: true, includeReader: true, cancellationToken);

        if (reader is IServedObject served) {
            return served;
        }

        if (reader is not null) {
            await reader.DisposeAsync();
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(
        StoreObjectWrite write,
        PageObjectOptions limits,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(limits);

        string createdAt = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        await using StorageDbContext database = Open();

        int written = await StoreObjectSql.UpsertAsync(database, write, createdAt, limits, cancellationToken);

        if (written == 0) {
            throw await RefusalAsync(database, write, limits, cancellationToken);
        }

        refresh?.Request();
    }

    /// <inheritdoc/>
    public async Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default) {
        string now = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        string browserThreshold = StoreSubjectKeys.Stamp(clock.GetUtcNow() - browserLifetime);
        await using StorageDbContext database = Open();

        int expired = await database.Database.ExecuteSqlAsync($"""
            DELETE FROM "StoreObjects"
            WHERE "ExpiresAt" IS NOT NULL AND "ExpiresAt" <= {now};
            """, cancellationToken);

        // A browser subject is stale only when nothing about it has been written within the browser's
        // lifetime. A value and a message decide the same question from their own table, so the check
        // looks at all three and an object is not dropped while either of the others is recent.
        int browsers = await database.Database.ExecuteSqlAsync($"""
            DELETE FROM "StoreObjects"
            WHERE "Subject" LIKE 'browser:%'
              AND "Subject" NOT IN (
                SELECT "Subject" FROM "StoreValues"
                WHERE "Subject" LIKE 'browser:%' AND "UpdatedAt" >= {browserThreshold}
                UNION
                SELECT "Subject" FROM "StoreMessages"
                WHERE "Subject" LIKE 'browser:%' AND "SentAt" >= {browserThreshold}
                UNION
                SELECT "Subject" FROM "StoreObjects"
                WHERE "Subject" LIKE 'browser:%' AND "CreatedAt" >= {browserThreshold});
            """, cancellationToken);

        if (expired + browsers > 0) {
            refresh?.Request();
        }

        return expired + browsers;
    }

    // The digest and the candidate subjects are bound; only the placeholder count and the two column
    // expressions this store wrote are part of the text.
    private async Task<PageObjectStream?> OpenCoreAsync(
        IReadOnlyList<string> subjects,
        string sha,
        bool includePage,
        bool includeReader,
        CancellationToken cancellationToken) {
        if (!includePage && !includeReader) {
            return null;
        }

        // The context is not scoped to this method: on success the stream owns it, and disposing it
        // here would close the connection under an open blob.
        StorageDbContext database = Open();
        sqlite3_blob? blob = null;
        try {
            await database.Database.OpenConnectionAsync(cancellationToken);
            SqliteConnection connection = (SqliteConnection)database.Database.GetDbConnection();
            List<Candidate> candidates = await CandidatesAsync(
                connection, subjects, sha, includePage, includeReader, cancellationToken);

            if (candidates.Count == 0) {
                await ReleaseAsync(database);
                return null;
            }

            Candidate chosen = candidates[0];
            // The handle is not an IDisposable; the stream that takes it closes it, and a failed open
            // closes it here, which is the ownership CA2000 cannot see.
#pragma warning disable CA2000
            int opened = raw.sqlite3_blob_open(
                connection.Handle, "main", "StoreObjects", "Content", chosen.RowId, 0, out blob);
#pragma warning restore CA2000

            if (opened != 0 || blob is null) {
                if (blob is not null) {
                    _ = raw.sqlite3_blob_close(blob);
                }

                await ReleaseAsync(database);
                return null;
            }

            long length = raw.sqlite3_blob_bytes(blob);
            SqliteStoreObjectStream stream = new(
                database, connection, blob, chosen.Subject, chosen.Sha, chosen.ContentType, length);
            blob = null; // The stream owns the handle from here.
            return stream;
        } catch {
            if (blob is not null) {
                _ = raw.sqlite3_blob_close(blob);
            }

            await ReleaseAsync(database);
            throw;
        }
    }

    private static async Task ReleaseAsync(StorageDbContext database) {
        if (database.Database.GetDbConnection().State == ConnectionState.Open) {
            await database.Database.CloseConnectionAsync();
        }

        await database.DisposeAsync();
    }

    private async Task<List<Candidate>> CandidatesAsync(
        SqliteConnection connection,
        IReadOnlyList<string> subjects,
        string sha,
        bool includePage,
        bool includeReader,
        CancellationToken cancellationToken) {
        string subjectList = Placeholders(subjects);
        string addresses = (includePage, includeReader, subjects.Count > 0) switch {
            (true, true, true) => $"""("Subject" LIKE 'page:%' OR "Subject" IN ({subjectList}))""",
            (true, true, false) => """("Subject" LIKE 'page:%')""",
            (true, false, _) => """("Subject" LIKE 'page:%')""",
            (false, true, true) => $"""("Subject" IN ({subjectList}))""",
            _ => "0",
        };

        await using DbCommand command = connection.CreateCommand();
#pragma warning disable CA2100 // The only text is this store's own column names and generated placeholders.
        command.CommandText = $"""
            SELECT "rowid", "Subject", "Sha", "ContentType"
            FROM "StoreObjects"
            WHERE "Sha" = $sha
              AND {addresses}
              AND ("ExpiresAt" IS NULL OR "ExpiresAt" > $now)
            ORDER BY CASE WHEN "Subject" LIKE 'page:%' THEN 0 ELSE 1 END, "Subject";
            """;
#pragma warning restore CA2100
        Add(command, "$sha", sha);
        Add(command, "$now", StoreSubjectKeys.Stamp(clock.GetUtcNow()));
        for (int index = 0; index < subjects.Count; index++) {
            Add(command, "$s" + index.ToString(CultureInfo.InvariantCulture), subjects[index]);
        }

        List<Candidate> candidates = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) {
            candidates.Add(new Candidate(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return Rank(candidates, subjects);
    }

    private static List<Candidate> Rank(List<Candidate> candidates, IReadOnlyList<string> subjects) {
        if (subjects.Count == 0) {
            return candidates;
        }

        // The query puts a page subject first, because a public object and a private one with the
        // same bytes are the same bytes. Among the reader's own, the most ephemeral subject wins.
        candidates.Sort((left, right) => {
            int byPage = PageRank(left.Subject).CompareTo(PageRank(right.Subject));
            return byPage != 0 ? byPage : SubjectRank(subjects, left.Subject).CompareTo(SubjectRank(subjects, right.Subject));
        });

        return candidates;
    }

    private static int PageRank(string subject) =>
        subject.StartsWith(StoreSubjectKeys.PagePrefix, StringComparison.Ordinal) ? 0 : 1;

    private static int SubjectRank(IReadOnlyList<string> subjects, string subject) {
        for (int index = 0; index < subjects.Count; index++) {
            if (string.Equals(subjects[index], subject, StringComparison.Ordinal)) {
                return index;
            }
        }

        return subjects.Count;
    }

    private static string Placeholders(IReadOnlyList<string> subjects) =>
        string.Join(", ", Enumerable.Range(0, subjects.Count)
            .Select(index => "$s" + index.ToString(CultureInfo.InvariantCulture)));

    private static void Add(DbCommand command, string name, object value) {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task<ArgumentException> RefusalAsync(
        StorageDbContext database,
        StoreObjectWrite write,
        PageObjectOptions limits,
        CancellationToken cancellationToken) {
        if (write.RequireLivePage) {
            string pageKey = write.Subject[StoreSubjectKeys.PagePrefix.Length..];
            if (!await database.Set<PageHead>().AnyAsync(
                head => head.Path == pageKey && !head.IsDeleted, cancellationToken)) {
                return new ArgumentException(
                    $"{pageKey} was deleted before its object could be written; the write was refused.");
            }
        }

        if (write.Length > limits.MaxObjectBytes) {
            return new ArgumentException(
                $"The object is {write.Length} bytes and Pages:Stores:Objects:MaxObjectBytes is {limits.MaxObjectBytes}.");
        }

        long held = await database.Set<StoreObjectEntry>()
            .Where(row => row.Subject == write.Subject && row.Sha != write.Sha)
            .SumAsync(row => row.Length, cancellationToken);

        return new ArgumentException(
            $"The subject would need {held + write.Length} bytes of objects and may use {limits.MaxSubjectBytes}.");
    }

    private StorageDbContext Open() {
        StorageDbContext database = contexts.CreateDbContext();
        database.Database.SetCommandTimeout(options.CommandTimeout);

        return database;
    }

    private sealed record Candidate(long RowId, string Subject, string Sha, string ContentType);
}
