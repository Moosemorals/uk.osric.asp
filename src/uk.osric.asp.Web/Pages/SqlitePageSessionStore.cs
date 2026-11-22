// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core implementation of the session identity.</summary>
/// <remarks>
/// Every call owns its context, as the value store does: a page's task can outlive the request
/// scope. A session that ends — deleted, expired or evicted — takes its <c>session:</c> subject's
/// values with it, in the same transaction, because a value whose lifetime is the session must not
/// outlive it.
/// </remarks>
internal sealed class SqlitePageSessionStore(
    IDbContextFactory<StorageDbContext> contexts,
    PageStoresOptions options,
    AuthenticationOptions authentication,
    TimeProvider clock) : IPageSessionStore {

    // One mint pays for a batch of deletes rather than one row, so a burst of new sessions does not
    // cost an eviction per insert. The cap is small enough that a hundredth of it is a handful.
    private int EvictionBatchSize => Math.Clamp(options.Visitor.MaxLiveSessions / 100 + 1, 1, 64);

    // The signed-in row lives as long as the sign-in it belongs to, which is the ticket's lifetime.
    private TimeSpan AnonymousIdle => options.Visitor.AnonymousIdle;

    private TimeSpan SignedInIdle => authentication.TicketLifetime;

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

        while (await CountAsync(database, cancellationToken) >= options.Visitor.MaxLiveSessions) {
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
    public async Task<bool> AdoptAsync(
        string? previousSessionId,
        string account,
        string sessionId,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        await using StorageDbContext database = Open();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        if (previousSessionId is not null) {
            // The person and one-shot entries follow the visitor; what is left belongs to the session
            // the rotation ends, so it goes with the old row.
            _ = await StoreValueSql.AdoptAsync(database, previousSessionId, account, cancellationToken);
            _ = await StoreValueSql.DeleteSubjectAsync(
                database, StoreSubjects.ForSession(previousSessionId), cancellationToken);
            _ = await database.Set<PageSessionEntry>()
                .Where(row => row.SessionId == previousSessionId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        _ = await DeleteExpiredAsync(database, now, cancellationToken);
        while (await CountAsync(database, cancellationToken) >= options.Visitor.MaxLiveSessions) {
            (int anonymous, int signedIn) = await EvictBatchAsync(database, cancellationToken);
            if (anonymous + signedIn == 0) {
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
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        _ = await StoreValueSql.DeleteSessionsAsync(database, [sessionId], cancellationToken);
        bool deleted = await database.Set<PageSessionEntry>()
            .Where(row => row.SessionId == sessionId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    /// <inheritdoc/>
    public async Task<PageSessionSweep> RemoveExpiredAsync(CancellationToken cancellationToken = default) {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        await using StorageDbContext database = Open();
        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        int expired = await DeleteExpiredAsync(database, now, cancellationToken);
        int anonymous = 0;
        int signedIn = 0;

        while (await CountAsync(database, cancellationToken) > options.Visitor.MaxLiveSessions) {
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

    // The rows that are about to end are read first, so their values go with them in the same
    // transaction; a row deleted without this would leave a session subject with no session.
    private async Task<int> DeleteExpiredAsync(
        StorageDbContext database,
        DateTime now,
        CancellationToken cancellationToken) {
        string anonymousThreshold = Format(now - AnonymousIdle);
        string signedInThreshold = Format(now - SignedInIdle);

        // The stored timestamp is fixed-width UTC text and the threshold is the same shape, so the
        // comparison is the SQL the old store used; a LINQ DateTime compare would mix formats.
        string[] expired = await database.Database
            .SqlQuery<string>($"""
                SELECT "SessionId" AS "Value" FROM "PageSessions"
                WHERE ("Account" IS NULL AND "UpdatedAt" < {anonymousThreshold})
                   OR ("Account" IS NOT NULL AND "UpdatedAt" < {signedInThreshold})
                """)
            .ToArrayAsync(cancellationToken);

        if (expired.Length == 0) {
            return 0;
        }

        _ = await StoreValueSql.DeleteSessionsAsync(database, expired, cancellationToken);

        return await database.Database.ExecuteSqlAsync($"""
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
        _ = await StoreValueSql.DeleteSessionsAsync(database, ids, cancellationToken);
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

    private StorageDbContext Open() {
        StorageDbContext database = contexts.CreateDbContext();
        database.Database.SetCommandTimeout(options.CommandTimeout);

        return database;
    }
}
