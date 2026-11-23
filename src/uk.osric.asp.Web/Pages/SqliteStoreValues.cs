// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Globalization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core and raw-SQL implementation of <see cref="IStoreValues"/>.</summary>
/// <remarks>
/// <para>
/// A read is a handful of indexed rows resolved in memory: a subject holds at most its key limit, so the
/// obvious scan is the right one. The two statements that must be single — the take and the delete —
/// are raw SQL with one <c>DELETE</c> each, so nothing can take a one-shot twice.
/// </para>
/// <para>
/// Every call owns its context, as the stores it replaces did: a render that was abandoned can
/// outlive the request scope, and a page's task must never touch a disposed context.
/// </para>
/// </remarks>
internal sealed class SqliteStoreValues(
    IDbContextFactory<StorageDbContext> contexts,
    PageStoresOptions options,
    TimeProvider clock,
    PageStorageRefresh? refresh = null) : IStoreValues {

    /// <inheritdoc/>
    public async Task<StoreValueRow?> FindAsync(
        IReadOnlyList<string> subjects,
        string? pageKey,
        string key,
        StoreLifetimeKind? lifetime = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (subjects.Count == 0) {
            return null;
        }

        string now = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        await using StorageDbContext database = Open();

        List<StoreValueEntry> rows = await database.Set<StoreValueEntry>().AsNoTracking()
            .Where(row => subjects.Contains(row.Subject)
                && row.Key == key
                && (pageKey == null || row.PageKey == pageKey)
                && (lifetime == null || row.Lifetime == (int)lifetime))
            .ToListAsync(cancellationToken);

        StoreValueEntry? found = rows
            .Where(row => IsLive(row.ExpiresAt, now))
            .OrderBy(row => Rank(subjects, row.Subject))
            .ThenBy(row => row.PageKey, StringComparer.Ordinal)
            .FirstOrDefault();

        return found is null ? null : Row(found);
    }

    /// <inheritdoc/>
    public async Task<StoreValueRow?> TakeAsync(
        IReadOnlyList<string> subjects,
        string? pageKey,
        string key,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (subjects.Count == 0) {
            return null;
        }

        await using StorageDbContext database = Open();
        await database.Database.OpenConnectionAsync(cancellationToken);
        try {
            await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // The placeholders are generated; every value is a bound parameter.
            command.CommandText = $"""
                DELETE FROM "StoreValues"
                WHERE "rowid" = (
                    SELECT "rowid" FROM "StoreValues"
                    WHERE "Subject" IN ({Placeholders(subjects)})
                      AND "Key" = $key
                      AND "Lifetime" = $lifetime
                      AND ($pageKey IS NULL OR "PageKey" = $pageKey)
                      AND ("ExpiresAt" IS NULL OR "ExpiresAt" > $now)
                    ORDER BY "PageKey" LIMIT 1)
                RETURNING "Subject", "PageKey", "Key", "Lifetime", "ExpiresAt", "Value", "UpdatedAt";
                """;
#pragma warning restore CA2100
            BindSubjects(command, subjects);
            Add(command, "$key", key);
            Add(command, "$lifetime", (int)StoreLifetimeKind.NextRead);
            Add(command, "$pageKey", pageKey);
            Add(command, "$now", StoreSubjectKeys.Stamp(clock.GetUtcNow()));

            StoreValueRow? taken = await ReadOneAsync(command, cancellationToken);
            if (taken is not null) {
                refresh?.Request();
            }

            return taken;
        } finally {
            await database.Database.CloseConnectionAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StoreValueRow>> ListAsync(
        IReadOnlyList<string> subjects,
        string? pageKey,
        StoreLifetimeKind? lifetime,
        int limit,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if (subjects.Count == 0) {
            return [];
        }

        string now = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        await using StorageDbContext database = Open();

        List<StoreValueEntry> rows = await database.Set<StoreValueEntry>().AsNoTracking()
            .Where(row => subjects.Contains(row.Subject) && (pageKey == null || row.PageKey == pageKey))
            .ToListAsync(cancellationToken);

        HashSet<string> seen = [with(StringComparer.Ordinal)];
        List<StoreValueRow> answer = [];
        foreach (StoreValueEntry row in rows
            .Where(row => IsLive(row.ExpiresAt, now) && (lifetime is null || row.Lifetime == (int)lifetime))
            .OrderBy(row => Rank(subjects, row.Subject))
            .ThenBy(row => row.PageKey, StringComparer.Ordinal)
            .ThenBy(row => row.Key, StringComparer.Ordinal)) {
            if (seen.Add(row.PageKey + "\n" + row.Key)) {
                answer.Add(Row(row));
                if (answer.Count >= limit) {
                    break;
                }
            }
        }

        return answer;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(
        StoreValueWrite write,
        SubjectLimits limits,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(limits);

        string updatedAt = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        await using StorageDbContext database = Open();

        int written = await StoreValueSql.UpsertAsync(database, write, updatedAt, limits, cancellationToken);

        if (written == 0) {
            throw await RefusalAsync(database, write, limits, cancellationToken);
        }

        refresh?.Request();
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(
        IReadOnlyList<string> subjects,
        string pageKey,
        string key,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentException.ThrowIfNullOrEmpty(pageKey);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (subjects.Count == 0) {
            return false;
        }

        await using StorageDbContext database = Open();
        await database.Database.OpenConnectionAsync(cancellationToken);
        try {
            await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // The placeholders are generated; every value is a bound parameter.
            command.CommandText = $"""
                DELETE FROM "StoreValues"
                WHERE "rowid" = (
                    SELECT "rowid" FROM "StoreValues"
                    WHERE "Subject" IN ({Placeholders(subjects)})
                      AND "PageKey" = $pageKey
                      AND "Key" = $key
                    ORDER BY "PageKey" LIMIT 1);
                """;
#pragma warning restore CA2100
            BindSubjects(command, subjects);
            Add(command, "$pageKey", pageKey);
            Add(command, "$key", key);
            int removed = await command.ExecuteNonQueryAsync(cancellationToken);
            if (removed > 0) {
                refresh?.Request();
            }

            return removed > 0;
        } finally {
            await database.Database.CloseConnectionAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default) {
        string now = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        string browserThreshold = StoreSubjectKeys.Stamp(clock.GetUtcNow() - browserLifetime);
        await using StorageDbContext database = Open();

        int expired = await database.Database.ExecuteSqlAsync($"""
            DELETE FROM "StoreValues"
            WHERE "ExpiresAt" IS NOT NULL AND "ExpiresAt" <= {now};
            """, cancellationToken);

        int browsers = await database.Database.ExecuteSqlAsync($"""
            DELETE FROM "StoreValues"
            WHERE "Subject" IN (
                SELECT "Subject" FROM "StoreValues"
                WHERE "Subject" LIKE 'browser:%'
                GROUP BY "Subject"
                HAVING MAX("UpdatedAt") < {browserThreshold});
            """, cancellationToken);

        if (expired + browsers > 0) {
            refresh?.Request();
        }

        return expired + browsers;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StoreValueRow>> GetPageValuesAsync(
        PagePath page,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);

        await using StorageDbContext database = Open();
        List<StoreValueEntry> rows = await database.Set<StoreValueEntry>().AsNoTracking()
            .Where(row => row.Subject == StoreSubjectKeys.ForPage(page)
                && row.Lifetime == (int)StoreLifetimeKind.Page
                && row.ExpiresAt == null)
            .OrderBy(row => row.Key)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(Row)];
    }

    /// <inheritdoc/>
    public Task WritePageValueAsync(
        PagePath page,
        string key,
        string value,
        SubjectLimits limits,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);

        return WriteAsync(
            new StoreValueWrite(
                StoreSubjectKeys.ForPage(page),
                page.Key,
                key,
                (int)StoreLifetimeKind.Page,
                ExpiresAt: null,
                value,
                RequireLivePage: true),
            limits,
            cancellationToken);
    }

    // The failed statement already decided the outcome, so this only names the rule. A re-read can
    // disagree with the write if another request changed the subject in between; the message is
    // diagnostic, the statement is the authority.
    private static async Task<ArgumentException> RefusalAsync(
        StorageDbContext database,
        StoreValueWrite write,
        SubjectLimits limits,
        CancellationToken cancellationToken) {
        if (write.RequireLivePage
            && !await database.Set<PageHead>().AnyAsync(
                head => head.Path == write.PageKey && !head.IsDeleted, cancellationToken)) {
            return new ArgumentException($"{write.PageKey} was deleted before its state could be written; the write was refused.");
        }

        int keys = await database.Set<StoreValueEntry>()
            .CountAsync(row => row.Subject == write.Subject, cancellationToken);
        long bytes = await database.Set<StoreValueEntry>()
            .Where(row => row.Subject == write.Subject)
            .SumAsync(row => (long)row.Key.Length + row.Value.Length, cancellationToken);

        return keys >= limits.MaxKeys
            ? new ArgumentException($"The subject already holds {keys} keys and may hold {limits.MaxKeys}.")
            : new ArgumentException($"The subject would need {bytes} bytes of state and may use {limits.MaxTotalBytes}.");
    }

    private static StoreValueRow Row(StoreValueEntry entry) => new(
        entry.Subject, entry.PageKey, entry.Key, entry.Lifetime, entry.ExpiresAt, entry.Value, entry.UpdatedAt);

    private static bool IsLive(string? expiresAt, string now) =>
        expiresAt is null || string.CompareOrdinal(expiresAt, now) > 0;

    private static int Rank(IReadOnlyList<string> subjects, string subject) {
        for (int index = 0; index < subjects.Count; index++) {
            if (string.Equals(subjects[index], subject, StringComparison.Ordinal)) {
                return index;
            }
        }

        return subjects.Count;
    }

    private static string Placeholders(IReadOnlyList<string> subjects) =>
        string.Join(", ", Enumerable.Range(0, subjects.Count).Select(index => "$s" + index.ToString(CultureInfo.InvariantCulture)));

    private static void BindSubjects(DbCommand command, IReadOnlyList<string> subjects) {
        for (int index = 0; index < subjects.Count; index++) {
            Add(command, "$s" + index.ToString(CultureInfo.InvariantCulture), subjects[index]);
        }
    }

    private static void Add(DbCommand command, string name, object? value) {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static async Task<StoreValueRow?> ReadOneAsync(DbCommand command, CancellationToken cancellationToken) {
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) {
            return null;
        }

        return new StoreValueRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private StorageDbContext Open() {
        StorageDbContext database = contexts.CreateDbContext();
        database.Database.SetCommandTimeout(options.CommandTimeout);

        return database;
    }
}
