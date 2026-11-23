// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Storage;

/// <summary>The object statements a caller with its own transaction needs.</summary>
/// <remarks>
/// The write proves both rules in the one statement that stores: the subject's byte total, and, for a
/// Page write, that the page still has a live head. A page's delete and move, a session's end, an
/// account's adoption and a removal each own a transaction, and the store's seam opens its own
/// context, so these statements live here for the callers to run inside theirs.
/// </remarks>
internal static class StoreObjectSql {

    /// <summary>Writes one object under its subject's limits, in one statement.</summary>
    /// <param name="database">The context whose transaction the write belongs to.</param>
    /// <param name="write">The row to write.</param>
    /// <param name="createdAt">The fixed-width timestamp to store.</param>
    /// <param name="limits">The limits the subject obeys.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were written: 1 on success, 0 when a limit or a missing live page refused it.</returns>
    public static Task<int> UpsertAsync(
        StorageDbContext database,
        StoreObjectWrite write,
        string createdAt,
        PageObjectOptions limits,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(limits);

        // Replacing the same digest does not make the subject any larger, so the row being replaced
        // is left out of both totals. `Length` is the store's own column, written by this statement.
        string pageKey = write.RequireLivePage
            ? write.Subject[StoreSubjectKeys.PagePrefix.Length..]
            : string.Empty;

        return database.Database.ExecuteSqlAsync($"""
            INSERT INTO "StoreObjects" ("Subject", "Sha", "Length", "ContentType", "Lifetime", "ExpiresAt", "Content", "CreatedAt")
            SELECT {write.Subject}, {write.Sha}, {write.Length}, {write.ContentType}, {write.Lifetime}, {write.ExpiresAt}, {write.Content}, {createdAt}
            WHERE ({(write.RequireLivePage ? 1 : 0)} = 0
                    OR EXISTS (SELECT 1 FROM "PageHeads" WHERE "Path" = {pageKey} AND "IsDeleted" = 0))
              AND {write.Length} <= {limits.MaxObjectBytes}
              AND (SELECT COALESCE(SUM("Length"), 0) FROM "StoreObjects"
                    WHERE "Subject" = {write.Subject} AND "Sha" <> {write.Sha}) + {write.Length} <= {limits.MaxSubjectBytes}
            ON CONFLICT ("Subject", "Sha") DO UPDATE SET
                "Length" = excluded."Length",
                "ContentType" = excluded."ContentType",
                "Lifetime" = excluded."Lifetime",
                "ExpiresAt" = excluded."ExpiresAt",
                "Content" = excluded."Content",
                "CreatedAt" = excluded."CreatedAt"
            WHERE excluded."Length" <= {limits.MaxObjectBytes}
              AND (SELECT COALESCE(SUM("Length"), 0) FROM "StoreObjects"
                    WHERE "Subject" = excluded."Subject" AND "Sha" <> excluded."Sha") + excluded."Length" <= {limits.MaxSubjectBytes};
            """, cancellationToken);
    }

    /// <summary>Removes every object a page's Page subject holds.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="key">The page's canonical key.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were removed.</returns>
    public static Task<int> DeletePageAsync(StorageDbContext database, PagePath key, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(key);

        return DeleteSubjectAsync(database, StoreSubjectKeys.ForPage(key), cancellationToken);
    }

    /// <summary>Re-keys a page's Page subject objects to a new key.</summary>
    /// <param name="database">The context whose transaction the move belongs to.</param>
    /// <param name="from">The key the page has.</param>
    /// <param name="to">The key it should have.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were re-keyed.</returns>
    public static Task<int> MovePageAsync(
        StorageDbContext database,
        PagePath from,
        PagePath to,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return database.Database.ExecuteSqlAsync($"""
            UPDATE "StoreObjects"
            SET "Subject" = {StoreSubjectKeys.ForPage(to)}
            WHERE "Subject" = {StoreSubjectKeys.ForPage(from)};
            """, cancellationToken);
    }

    /// <summary>Removes every object one subject holds.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="subject">The subject string.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were removed.</returns>
    public static Task<int> DeleteSubjectAsync(
        StorageDbContext database,
        string subject,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        return database.Database.ExecuteSqlAsync(
            $"""DELETE FROM "StoreObjects" WHERE "Subject" = {subject};""", cancellationToken);
    }

    /// <summary>Removes every object held by a set of sessions.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="subjects">The subjects that are ending.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were removed.</returns>
    public static Task<int> DeleteSubjectsAsync(
        StorageDbContext database,
        IReadOnlyList<string> subjects,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(subjects);

        return subjects.Count == 0
            ? Task.FromResult(0)
            : database.Set<StoreObjectEntry>()
                .Where(row => subjects.Contains(row.Subject))
                .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Re-keys an anonymous session's person objects into the account.</summary>
    /// <param name="database">The context whose transaction the adoption belongs to.</param>
    /// <param name="sessionId">The session the visitor is leaving.</param>
    /// <param name="account">The normalised account name being signed in to.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many objects were adopted or already held by the account.</returns>
    /// <remarks>
    /// Only <see cref="StoreLifetimeKind.Person"/> moves: a session object belongs to the visit the
    /// rotation ends, and a browser object belongs to the device and is never adopted. Content
    /// addressing makes a collision likely — the account may already hold the same bytes — so the
    /// session's copy goes before the re-key rather than failing the sign-in's transaction on the
    /// primary key; the account's row, and its lifetime, win.
    /// </remarks>
    public static async Task<int> AdoptAsync(
        StorageDbContext database,
        string sessionId,
        string account,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        string from = StoreSubjectKeys.ForSession(sessionId);
        string to = StoreSubjectKeys.ForAccount(account);

        // The object is identical by digest, so which row survives changes nothing a reader can see;
        // keeping the account's row keeps the lifetime it already had.
        int duplicates = await database.Database.ExecuteSqlAsync($"""
            DELETE FROM "StoreObjects"
            WHERE "Subject" = {from}
              AND "Lifetime" = {(int)StoreLifetimeKind.Person}
              AND EXISTS (
                SELECT 1 FROM "StoreObjects" AS "Held"
                WHERE "Held"."Subject" = {to} AND "Held"."Sha" = "StoreObjects"."Sha");
            """, cancellationToken);

        int adopted = await database.Database.ExecuteSqlAsync($"""
            UPDATE "StoreObjects"
            SET "Subject" = {to}
            WHERE "Subject" = {from}
              AND "Lifetime" = {(int)StoreLifetimeKind.Person};
            """, cancellationToken);

        return duplicates + adopted;
    }
}
