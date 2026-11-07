// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using System.Globalization;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core implementation of one page's key/value store.</summary>
/// <remarks>
/// Every call creates and disposes its own <see cref="StorageDbContext"/>: a render that was
/// abandoned can outlive the request scope, and a page's task must never touch a disposed one.
/// </remarks>
internal sealed class SqlitePageStateStore(
    IDbContextFactory<StorageDbContext> contexts,
    PageStorageOptions options,
    TimeProvider clock,
    PageStorageRefresh? refresh = null) : IPageStateStore {

    /// <inheritdoc/>
    public async Task<string?> GetValueAsync(PagePath path, string key, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);

        await using StorageDbContext database = Open();

        return await database.Set<PageStateEntry>()
            .Where(row => row.PagePath == path.Key && row.Key == key)
            .Select(row => row.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SetValueAsync(PagePath path, string key, string value, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);

        await using StorageDbContext database = Open();

        int written = await UpsertAsync(database, path, key, value, clock.GetUtcNow(), options, cancellationToken);
        if (written == 0) {
            throw await RefusalAsync(database, path, cancellationToken);
        }

        refresh?.Request();
    }

    /// <summary>Writes one value under every per-page cap, in one statement.</summary>
    /// <param name="database">The context the write runs in, which may be inside a batch transaction.</param>
    /// <param name="path">The page whose state is written.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value as JSON.</param>
    /// <param name="updatedAt">The row's timestamp.</param>
    /// <param name="options">The caps the statement enforces.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many rows were written: 1 on success, 0 when a cap or a missing live page refused it.</returns>
    /// <remarks>
    /// Both paths that write state — a page's own <see cref="SetValueAsync"/> and an import's batch —
    /// go through this statement, so the columns, the conflict target and the caps cannot drift.
    /// </remarks>
    internal static Task<int> UpsertAsync(
        StorageDbContext database,
        PagePath path,
        string key,
        string value,
        DateTimeOffset updatedAt,
        PageStorageOptions options,
        CancellationToken cancellationToken) {
        long added = KeyBytes(key) + KeyBytes(value);

        // One statement carries every rule: the head must still be live, the page must stay within its
        // key count, and the total must stay within its byte cap. A concurrent writer therefore either
        // fits or is refused; it cannot slip over the cap through a read-then-write race in the host.
        return database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PageState" ("PagePath", "Key", "Value", "UpdatedAt")
            SELECT {path.Key}, {key}, {value}, {updatedAt.ToString("O", CultureInfo.InvariantCulture)}
            WHERE EXISTS (
                    SELECT 1 FROM "PageHeads" WHERE "Path" = {path.Key} AND "IsDeleted" = 0)
              AND (EXISTS (
                    SELECT 1 FROM "PageState" WHERE "PagePath" = {path.Key} AND "Key" = {key})
                OR (SELECT COUNT(*) FROM "PageState" WHERE "PagePath" = {path.Key}) < {options.MaxKeysPerPage})
              AND (SELECT COALESCE(SUM(length(CAST("Key" AS BLOB)) + length(CAST("Value" AS BLOB))), 0)
                    FROM "PageState" WHERE "PagePath" = {path.Key} AND "Key" <> {key}) + {added} <= {options.MaxPageBytes}
            ON CONFLICT ("PagePath", "Key") DO UPDATE SET
                "Value" = excluded."Value",
                "UpdatedAt" = excluded."UpdatedAt"
            WHERE (SELECT COALESCE(SUM(length(CAST("Key" AS BLOB)) + length(CAST("Value" AS BLOB))), 0)
                    FROM "PageState" WHERE "PagePath" = {path.Key} AND "Key" <> {key}) + {added} <= {options.MaxPageBytes};
            """, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteValueAsync(PagePath path, string key, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);

        await using StorageDbContext database = Open();
        int deleted = await database.Set<PageStateEntry>()
            .Where(row => row.PagePath == path.Key && row.Key == key)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0) {
            refresh?.Request();
        }

        return deleted > 0;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListKeysAsync(PagePath path, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);

        await using StorageDbContext database = Open();
        List<string> keys = await database.Set<PageStateEntry>()
            .Where(row => row.PagePath == path.Key)
            .Select(row => row.Key)
            .ToListAsync(cancellationToken);

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageStateValue>> GetValuesAsync(PagePath path, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);

        await using StorageDbContext database = Open();
        var rows = await database.Set<PageStateEntry>().AsNoTracking()
            .Where(row => row.PagePath == path.Key)
            .Select(row => new { row.Key, row.Value })
            .ToArrayAsync(cancellationToken);

        return [.. rows
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .Select(row => new PageStateValue(row.Key, row.Value))];
    }

    // The failed statement already decided the outcome, so this only names the rule. A re-read can
    // disagree with the write if another request changed the page in between; the message is
    // diagnostic, the statement is the authority.
    private async Task<ArgumentException> RefusalAsync(
        StorageDbContext database,
        PagePath path,
        CancellationToken cancellationToken) {
        bool live = await database.Set<PageHead>()
            .AnyAsync(head => head.Path == path.Key && !head.IsDeleted, cancellationToken);

        if (!live) {
            return new ArgumentException(
                $"{path.Key} was deleted before its state could be written; the write was refused.", "key");
        }

        int keys = await database.Set<PageStateEntry>()
            .CountAsync(row => row.PagePath == path.Key, cancellationToken);
        long bytes = await database.Set<PageStateEntry>()
            .Where(row => row.PagePath == path.Key)
            .SumAsync(row => (long)row.Key.Length + row.Value.Length, cancellationToken);

        return keys >= options.MaxKeysPerPage
            ? new ArgumentException(
                $"{path.Key} already holds {keys} keys and may hold {options.MaxKeysPerPage}.", "key")
            : new ArgumentException(
                $"{path.Key} would need {bytes} bytes of state and may use {options.MaxPageBytes}.", "key");
    }

    // The SQL measures bytes, so the host does too, rather than counting characters and hoping.
    private static long KeyBytes(string text) => System.Text.Encoding.UTF8.GetByteCount(text);

    private StorageDbContext Open() {
        StorageDbContext database = contexts.CreateDbContext();
        database.Database.SetCommandTimeout(options.CommandTimeout);

        return database;
    }
}
