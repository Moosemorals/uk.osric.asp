// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;
using System.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core implementation of one visitor's bounded key/value store.</summary>
/// <remarks>
/// Every call creates and disposes its own <see cref="StorageDbContext"/>, as page state does: a
/// render that was abandoned can outlive the request scope, and a page's task must never touch a
/// disposed context.
/// </remarks>
internal sealed class SqlitePageSessionStore(
    IDbContextFactory<StorageDbContext> contexts,
    PageSessionOptions options,
    TimeProvider clock) : IPageSessionStore {

    // One mint pays for a batch of deletes rather than one row, so a burst of new sessions does not
    // cost an eviction per insert. The cap is small enough that a hundredth of it is a handful.
    private int EvictionBatchSize => Math.Clamp(options.MaxLiveSessions / 100 + 1, 1, 64);

    /// <inheritdoc/>
    public async Task<PageSessionEntry?> FindAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);

        await using StorageDbContext database = Open();

        return await database.Set<PageSessionEntry>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.SessionId == sessionId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> EnsureAsync(string sessionId, string? account, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        await using StorageDbContext database = Open();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        if (await database.Set<PageSessionEntry>().AnyAsync(row => row.SessionId == sessionId, cancellationToken)) {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        // A mint must never evict a live session because stale rows are holding the room.
        _ = await DeleteExpiredAsync(database, now, cancellationToken);

        while (await CountAsync(database, cancellationToken) >= options.MaxLiveSessions) {
            (int anonymous, int signedIn) = await EvictBatchAsync(database, cancellationToken);
            if (anonymous + signedIn == 0) {
                // The table is full and holds nothing that may be evicted, so the mint is refused.
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }

        string stamp = Format(now);
        _ = await database.Database.ExecuteSqlAsync($"""
            INSERT INTO "PageSessions" ("SessionId", "Account", "CreatedAt", "UpdatedAt")
            VALUES ({sessionId}, {account}, {stamp}, {stamp});
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc/>
    public async Task TouchAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);

        await using StorageDbContext database = Open();
        _ = await database.Database.ExecuteSqlAsync($"""
            UPDATE "PageSessions" SET "UpdatedAt" = {Format(clock.GetUtcNow().UtcDateTime)} WHERE "SessionId" = {sessionId};
            """, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);

        await using StorageDbContext database = Open();

        return await database.Set<PageSessionEntry>()
            .Where(row => row.SessionId == sessionId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    /// <inheritdoc/>
    public async Task<string?> GetValueAsync(
        string sessionId,
        string pagePath,
        string key,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(pagePath);

        await using StorageDbContext database = Open();

        return await database.Set<PageSessionValueEntry>()
            .Where(row => row.SessionId == sessionId && row.PagePath == pagePath && row.Key == key)
            .Select(row => row.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SetValueAsync(
        string sessionId,
        string pagePath,
        string key,
        string value,
        PageSessionCaps caps,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(pagePath);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(caps);

        await using StorageDbContext database = Open();

        int written = await UpsertAsync(database, sessionId, pagePath, key, value, caps, cancellationToken);
        if (written == 0) {
            throw await RefusalAsync(database, sessionId, pagePath, key, caps, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteValueAsync(
        string sessionId,
        string pagePath,
        string key,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(pagePath);

        await using StorageDbContext database = Open();

        return await database.Set<PageSessionValueEntry>()
            .Where(row => row.SessionId == sessionId && row.PagePath == pagePath && row.Key == key)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string sessionId,
        string pagePath,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(pagePath);

        await using StorageDbContext database = Open();
        List<string> keys = await database.Set<PageSessionValueEntry>()
            .Where(row => row.SessionId == sessionId && row.PagePath == pagePath)
            .Select(row => row.Key)
            .ToListAsync(cancellationToken);

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageSessionKey>> ListAllKeysAsync(
        string sessionId,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(sessionId);

        await using StorageDbContext database = Open();
        var rows = await database.Set<PageSessionValueEntry>().AsNoTracking()
            .Where(row => row.SessionId == sessionId)
            .Select(row => new { row.PagePath, row.Key })
            .ToArrayAsync(cancellationToken);

        return [.. rows
            .OrderBy(row => row.PagePath, StringComparer.Ordinal)
            .ThenBy(row => row.Key, StringComparer.Ordinal)
            .Select(row => new PageSessionKey(row.PagePath, row.Key))];
    }

    /// <inheritdoc/>
    public async Task<PageSessionSweep> RemoveExpiredAsync(CancellationToken cancellationToken = default) {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        await using StorageDbContext database = Open();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        int expired = await DeleteExpiredAsync(database, now, cancellationToken);
        int anonymous = 0;
        int signedIn = 0;

        while (await CountAsync(database, cancellationToken) > options.MaxLiveSessions) {
            (int evictedAnonymous, int evictedSignedIn) = await EvictBatchAsync(database, cancellationToken);
            if (evictedAnonymous + evictedSignedIn == 0) {
                break;
            }

            anonymous += evictedAnonymous;
            signedIn += evictedSignedIn;
        }

        await transaction.CommitAsync(cancellationToken);
        return new PageSessionSweep(expired, anonymous, signedIn);
    }

    /// <summary>Writes one value under every per-session cap, in one statement.</summary>
    /// <returns>How many rows were written: 1 on success, 0 when a cap or a missing session refused it.</returns>
    private static Task<int> UpsertAsync(
        StorageDbContext database,
        string sessionId,
        string pagePath,
        string key,
        string value,
        PageSessionCaps caps,
        CancellationToken cancellationToken) {
        long added = Bytes(key) + Bytes(value);

        // One statement carries every rule: the session must still exist, it must stay within its
        // key count, and the total must stay within its byte cap. A concurrent writer therefore
        // either fits or is refused; it cannot slip over the cap through a read-then-write race.
        return database.Database.ExecuteSqlAsync($"""
            INSERT INTO "PageSessionValues" ("SessionId", "PagePath", "Key", "Value")
            SELECT {sessionId}, {pagePath}, {key}, {value}
            WHERE EXISTS (
                    SELECT 1 FROM "PageSessions" WHERE "SessionId" = {sessionId})
              AND (EXISTS (
                    SELECT 1 FROM "PageSessionValues"
                    WHERE "SessionId" = {sessionId} AND "PagePath" = {pagePath} AND "Key" = {key})
                OR (SELECT COUNT(*) FROM "PageSessionValues" WHERE "SessionId" = {sessionId}) < {caps.MaxKeys})
              AND (SELECT COALESCE(SUM(length(CAST("Key" AS BLOB)) + length(CAST("Value" AS BLOB))), 0)
                    FROM "PageSessionValues"
                    WHERE "SessionId" = {sessionId} AND NOT ("PagePath" = {pagePath} AND "Key" = {key})) + {added} <= {caps.MaxTotalBytes}
            ON CONFLICT ("SessionId", "PagePath", "Key") DO UPDATE SET
                "Value" = excluded."Value"
            WHERE (SELECT COALESCE(SUM(length(CAST("Key" AS BLOB)) + length(CAST("Value" AS BLOB))), 0)
                    FROM "PageSessionValues"
                    WHERE "SessionId" = {sessionId} AND NOT ("PagePath" = {pagePath} AND "Key" = {key})) + {added} <= {caps.MaxTotalBytes};
            """, cancellationToken);
    }

    // The failed statement already decided the outcome, so this only names the rule. A re-read can
    // disagree with the write if another request changed the session in between; the message is
    // diagnostic, the statement is the authority.
    private static async Task<ArgumentException> RefusalAsync(
        StorageDbContext database,
        string sessionId,
        string pagePath,
        string key,
        PageSessionCaps caps,
        CancellationToken cancellationToken) {
        bool live = await database.Set<PageSessionEntry>()
            .AnyAsync(row => row.SessionId == sessionId, cancellationToken);

        if (!live) {
            return new ArgumentException(
                $"The session for {pagePath} is no longer live; the write was refused.", nameof(key));
        }

        int keys = await database.Set<PageSessionValueEntry>()
            .CountAsync(row => row.SessionId == sessionId, cancellationToken);
        long bytes = await database.Set<PageSessionValueEntry>()
            .Where(row => row.SessionId == sessionId)
            .SumAsync(row => (long)row.Key.Length + row.Value.Length, cancellationToken);

        return keys >= caps.MaxKeys
            ? new ArgumentException($"The session already holds {keys} keys and may hold {caps.MaxKeys}.", nameof(key))
            : new ArgumentException($"The session would need {bytes} bytes of state and may use {caps.MaxTotalBytes}.", nameof(key));
    }

    // Rows are compared as ISO-8601 text, so a delete can carry the whole rule without a per-row
    // function call. Every timestamp this store writes is UTC and formatted the same way.
    private Task<int> DeleteExpiredAsync(
        StorageDbContext database,
        DateTime now,
        CancellationToken cancellationToken) {
        string anonymousThreshold = Format(now - options.AnonymousIdle);
        string signedInThreshold = Format(now - options.SignedInIdle);

        return database.Database.ExecuteSqlAsync($"""
            DELETE FROM "PageSessions"
            WHERE ("Account" IS NULL AND "UpdatedAt" < {anonymousThreshold})
               OR ("Account" IS NOT NULL AND "UpdatedAt" < {signedInThreshold});
            """, cancellationToken);
    }

    private static Task<int> CountAsync(StorageDbContext database, CancellationToken cancellationToken) =>
        database.Set<PageSessionEntry>().CountAsync(cancellationToken);

    // Anonymous sessions go first, oldest first; a signed-in session is only reached when no
    // anonymous one is left, which is what keeps a flood from costing the owner their own session.
    private async Task<(int Anonymous, int SignedIn)> EvictBatchAsync(
        StorageDbContext database,
        CancellationToken cancellationToken) {
        var victims = await database.Set<PageSessionEntry>().AsNoTracking()
            .OrderBy(row => row.Account == null ? 0 : 1)
            .ThenBy(row => row.UpdatedAt)
            .Take(EvictionBatchSize)
            .Select(row => new { row.SessionId, row.Account })
            .ToListAsync(cancellationToken);

        if (victims.Count == 0) {
            return (0, 0);
        }

        string[] ids = [.. victims.Select(victim => victim.SessionId)];
        _ = await database.Set<PageSessionEntry>()
            .Where(row => ids.Contains(row.SessionId))
            .ExecuteDeleteAsync(cancellationToken);

        return (victims.Count(victim => victim.Account is null),
            victims.Count(victim => victim.Account is not null));
    }

    // A fixed shape rather than round-trip "O": every value written is UTC, and a fixed-width string
    // is what makes the range deletes and the eviction order plain text comparisons.
    private static string Format(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static long Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private StorageDbContext Open() {
        StorageDbContext database = contexts.CreateDbContext();
        database.Database.SetCommandTimeout(options.CommandTimeout);

        return database;
    }
}
