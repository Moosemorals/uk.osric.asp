// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Globalization;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core and raw-SQL implementation of <see cref="IStoreMessages"/>.</summary>
/// <remarks>
/// <para>
/// The take and the peek are each one statement, which is the whole of the at-most-once promise: the
/// take deletes the row it selects, so two renders racing for a message answer one with it and one
/// with nothing. The peek is the same statement with a select, so it answers the row a take would
/// remove right now without reserving it.
/// </para>
/// <para>
/// Every call owns its context, as the value store does: a render that was abandoned can outlive the
/// request scope, and a page's task must never touch a disposed context.
/// </para>
/// </remarks>
internal sealed class SqliteStoreMessages(
    IDbContextFactory<StorageDbContext> contexts,
    PageStoresOptions options,
    TimeProvider clock,
    PageStorageRefresh? refresh = null) : IStoreMessages {

    /// <inheritdoc/>
    public async Task<bool> PostAsync(
        StoreMessageWrite write,
        PageMessageOptions limits,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(limits);

        string sentAt = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        await using StorageDbContext database = Open();

        // Both limits are proved in the statement that writes, so a concurrent poster either fits or is
        // refused rather than slipping over the limit through a read-then-write race in the host.
        int written = await database.Database.ExecuteSqlAsync($"""
            INSERT INTO "StoreMessages" ("Subject", "ToPageKey", "Topic", "FromPageKey", "Body", "Lifetime", "ExpiresAt", "SentAt")
            SELECT {write.Subject}, {write.ToPageKey}, {write.Topic}, {write.FromPageKey}, {write.Body}, {write.Lifetime}, {write.ExpiresAt}, {sentAt}
            WHERE (SELECT COUNT(*) FROM "StoreMessages" WHERE "Subject" = {write.Subject}) < {limits.MaxMessagesPerSubject}
              AND (SELECT COALESCE(SUM(length(CAST("Body" AS BLOB))), 0)
                    FROM "StoreMessages"
                    WHERE "Subject" = {write.Subject}) + length(CAST({write.Body} AS BLOB)) <= {limits.MaxSubjectBytes};
            """, cancellationToken);

        if (written > 0) {
            refresh?.Request();
        }

        return written > 0;
    }

    /// <inheritdoc/>
    public async Task<StoreMessageRow?> TakeAsync(
        IReadOnlyList<string> subjects,
        string? toPageKey,
        string? topic,
        CancellationToken cancellationToken = default) =>
        await ReadAsync(delete: true, subjects, toPageKey, topic, cancellationToken);

    /// <inheritdoc/>
    public async Task<StoreMessageRow?> PeekAsync(
        IReadOnlyList<string> subjects,
        string? toPageKey,
        string? topic,
        CancellationToken cancellationToken = default) =>
        await ReadAsync(delete: false, subjects, toPageKey, topic, cancellationToken);

    /// <inheritdoc/>
    public async Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default) {
        string now = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        string browserThreshold = StoreSubjectKeys.Stamp(clock.GetUtcNow() - browserLifetime);
        await using StorageDbContext database = Open();

        int expired = await database.Database.ExecuteSqlAsync($"""
            DELETE FROM "StoreMessages"
            WHERE "ExpiresAt" IS NOT NULL AND "ExpiresAt" <= {now};
            """, cancellationToken);

        // A browser subject is stale when neither of its tables has been written within the browser's
        // lifetime: the value's own sweep decides the same question from the other side, and a subject
        // with messages and no values is what this statement catches.
        int browsers = await database.Database.ExecuteSqlAsync($"""
            DELETE FROM "StoreMessages"
            WHERE "Subject" LIKE 'browser:%'
              AND "Subject" NOT IN (
                SELECT "Subject" FROM "StoreValues"
                WHERE "Subject" LIKE 'browser:%' AND "UpdatedAt" >= {browserThreshold}
                UNION
                SELECT "Subject" FROM "StoreMessages"
                WHERE "Subject" LIKE 'browser:%' AND "SentAt" >= {browserThreshold});
            """, cancellationToken);

        if (expired + browsers > 0) {
            refresh?.Request();
        }

        return expired + browsers;
    }

    private static StoreMessageRow Row(DbDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetInt32(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetString(8));

    private static string Placeholders(IReadOnlyList<string> subjects) =>
        string.Join(", ", Enumerable.Range(0, subjects.Count)
            .Select(index => "$s" + index.ToString(CultureInfo.InvariantCulture)));

    private static void Bind(DbCommand command, string name, object? value) {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    // The address column is one of two names this store writes, never text a page supplies; the
    // address itself is a bound parameter. That is why the statement is built here rather than the
    // two calls keeping a copy each.
    private async Task<StoreMessageRow?> ReadAsync(
        bool delete,
        IReadOnlyList<string> subjects,
        string? toPageKey,
        string? topic,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(subjects);

        if (subjects.Count == 0 || (toPageKey is null) == (topic is null)) {
            return null;
        }

        string column = topic is null ? "ToPageKey" : "Topic";
        string address = topic ?? toPageKey!;

        await using StorageDbContext database = Open();
        await database.Database.OpenConnectionAsync(cancellationToken);
        try {
            await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // The column name is one of this store's own two; every value is a bound parameter.
            command.CommandText = delete
                ? $"""
                    DELETE FROM "StoreMessages"
                    WHERE "Id" = (
                        SELECT "Id" FROM "StoreMessages"
                        WHERE "Subject" IN ({Placeholders(subjects)})
                          AND "{column}" = $address
                          AND ("ExpiresAt" IS NULL OR "ExpiresAt" > $now)
                        ORDER BY "Id" LIMIT 1)
                    RETURNING "Id", "Subject", "ToPageKey", "Topic", "FromPageKey", "Body", "Lifetime", "ExpiresAt", "SentAt";
                    """
                : $"""
                    SELECT "Id", "Subject", "ToPageKey", "Topic", "FromPageKey", "Body", "Lifetime", "ExpiresAt", "SentAt"
                    FROM "StoreMessages"
                    WHERE "Id" = (
                        SELECT "Id" FROM "StoreMessages"
                        WHERE "Subject" IN ({Placeholders(subjects)})
                          AND "{column}" = $address
                          AND ("ExpiresAt" IS NULL OR "ExpiresAt" > $now)
                        ORDER BY "Id" LIMIT 1);
                    """;
#pragma warning restore CA2100
            for (int index = 0; index < subjects.Count; index++) {
                Bind(command, "$s" + index.ToString(CultureInfo.InvariantCulture), subjects[index]);
            }

            Bind(command, "$address", address);
            Bind(command, "$now", StoreSubjectKeys.Stamp(clock.GetUtcNow()));

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) {
                return null;
            }

            StoreMessageRow row = Row(reader);
            if (delete) {
                refresh?.Request();
            }

            return row;
        } finally {
            await database.Database.CloseConnectionAsync();
        }
    }

    private StorageDbContext Open() {
        StorageDbContext database = contexts.CreateDbContext();
        database.Database.SetCommandTimeout(options.CommandTimeout);

        return database;
    }
}
