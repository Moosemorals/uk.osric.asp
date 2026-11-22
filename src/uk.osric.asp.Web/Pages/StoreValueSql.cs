// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Storage;

/// <summary>The statements that re-key or remove a subject, shared by every caller with a transaction.</summary>
/// <remarks>
/// A page's delete, move and scrub own a transaction of their own, and a session's sweep and an
/// account's removal own another. They cannot call the store's seam, because that opens its own
/// context, so the statements live here and every caller uses the same spelling of the subject. A
/// subject's ending covers its values, its messages and its declared rows: the helpers call every
/// statement, so a caller cannot forget one of the three.
/// </remarks>
internal static class StoreValueSql {

    /// <summary>Writes one value under its subject's caps, in one statement.</summary>
    /// <param name="database">The context whose transaction the write belongs to.</param>
    /// <param name="write">The row to write.</param>
    /// <param name="updatedAt">The fixed-width timestamp to store.</param>
    /// <param name="caps">The caps the subject obeys.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were written: 1 on success, 0 when a cap or a missing live page refused it.</returns>
    /// <remarks>
    /// One statement carries every rule: a Page write needs a live head, the subject must stay within
    /// its key count, and the total must stay within its byte cap. A concurrent writer therefore either
    /// fits or is refused; it cannot slip over the cap through a read-then-write race in the host.
    /// </remarks>
    public static Task<int> UpsertAsync(
        StorageDbContext database,
        StoreValueWrite write,
        string updatedAt,
        PageStoreCaps caps,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(caps);

        long added = Encoding.UTF8.GetByteCount(write.Key) + Encoding.UTF8.GetByteCount(write.Value);

        return database.Database.ExecuteSqlAsync($"""
            INSERT INTO "StoreValues" ("Subject", "PageKey", "Key", "Lifetime", "ExpiresAt", "Value", "UpdatedAt")
            SELECT {write.Subject}, {write.PageKey}, {write.Key}, {write.Lifetime}, {write.ExpiresAt}, {write.Value}, {updatedAt}
            WHERE ({(write.RequireLivePage ? 1 : 0)} = 0
                    OR EXISTS (SELECT 1 FROM "PageHeads" WHERE "Path" = {write.PageKey} AND "IsDeleted" = 0))
              AND (EXISTS (
                    SELECT 1 FROM "StoreValues"
                    WHERE "Subject" = {write.Subject} AND "PageKey" = {write.PageKey} AND "Key" = {write.Key})
                OR (SELECT COUNT(*) FROM "StoreValues" WHERE "Subject" = {write.Subject}) < {caps.MaxKeys})
              AND (SELECT COALESCE(SUM(length(CAST("Key" AS BLOB)) + length(CAST("Value" AS BLOB))), 0)
                    FROM "StoreValues"
                    WHERE "Subject" = {write.Subject}
                      AND NOT ("PageKey" = {write.PageKey} AND "Key" = {write.Key})) + {added} <= {caps.MaxTotalBytes}
            ON CONFLICT ("Subject", "PageKey", "Key") DO UPDATE SET
                "Lifetime" = excluded."Lifetime",
                "ExpiresAt" = excluded."ExpiresAt",
                "Value" = excluded."Value",
                "UpdatedAt" = excluded."UpdatedAt"
            WHERE (SELECT COALESCE(SUM(length(CAST("Key" AS BLOB)) + length(CAST("Value" AS BLOB))), 0)
                    FROM "StoreValues"
                    WHERE "Subject" = {write.Subject}
                      AND NOT ("PageKey" = {write.PageKey} AND "Key" = {write.Key})) + {added} <= {caps.MaxTotalBytes};
            """, cancellationToken);
    }

    /// <summary>Removes every value a page's Page subject holds.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="key">The page's canonical key.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were removed.</returns>
    public static async Task<int> DeletePageAsync(
        StorageDbContext database,
        PagePath key,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(key);

        int removed = await DeleteSubjectAsync(database, StoreSubjects.ForPage(key), cancellationToken);

        // A message addressed to the page belongs to whoever posted it, so it is not the Page
        // subject's to delete: every subject's message to this page goes with the page.
        return removed + await StoreMessageSql.DeletePageAsync(database, key, cancellationToken);
    }

    /// <summary>Re-keys a page's Page subject to a new key.</summary>
    /// <param name="database">The context whose transaction the move belongs to.</param>
    /// <param name="from">The key the page has.</param>
    /// <param name="to">The key it should have.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were re-keyed.</returns>
    public static async Task<int> MovePageAsync(
        StorageDbContext database,
        PagePath from,
        PagePath to,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        int moved = await database.Database.ExecuteSqlAsync($"""
            UPDATE "StoreValues"
            SET "Subject" = {StoreSubjects.ForPage(to)}, "PageKey" = {to.Key}
            WHERE "Subject" = {StoreSubjects.ForPage(from)};
            """, cancellationToken);

        return moved + await StoreMessageSql.MovePageAsync(database, from, to, cancellationToken);
    }

    /// <summary>Removes every value one subject holds.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="subject">The subject string.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were removed.</returns>
    public static async Task<int> DeleteSubjectAsync(
        StorageDbContext database,
        string subject,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        // One helper and not three: a subject's values, its messages and the rows it holds in a
        // declared table are the same ending, and a caller that forgot one would leave a stranger's
        // rows behind.
        int values = await database.Database.ExecuteSqlAsync(
            $"""DELETE FROM "StoreValues" WHERE "Subject" = {subject}""", cancellationToken);

        int messages = await StoreMessageSql.DeleteSubjectAsync(database, subject, cancellationToken);

        return values
            + messages
            + await DeclaredTableApplier.RemoveSubjectAsync(database, subject, cancellationToken);
    }

    /// <summary>Removes every value held by a set of sessions.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="sessionIds">The sessions that are ending.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were removed.</returns>
    public static async Task<int> DeleteSessionsAsync(
        StorageDbContext database,
        IReadOnlyList<string> sessionIds,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(sessionIds);

        if (sessionIds.Count == 0) {
            return 0;
        }

        string[] subjects = [.. sessionIds.Select(StoreSubjects.ForSession)];

        int removed = await database.Set<StoreValueEntry>()
            .Where(row => subjects.Contains(row.Subject))
            .ExecuteDeleteAsync(cancellationToken);

        removed += await StoreMessageSql.DeleteSubjectsAsync(database, subjects, cancellationToken);

        foreach (string subject in subjects) {
            removed += await DeclaredTableApplier.RemoveSubjectAsync(database, subject, cancellationToken);
        }

        return removed;
    }

    /// <summary>Re-keys an anonymous session's person and one-shot entries into the account.</summary>
    /// <param name="database">The context whose transaction the adoption belongs to.</param>
    /// <param name="sessionId">The session the visitor is leaving.</param>
    /// <param name="account">The normalised account name being signed in to.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were adopted.</returns>
    /// <remarks>
    /// Only <see cref="StoreLifetimeKind.Person"/> and <see cref="StoreLifetimeKind.NextRead"/> move:
    /// a <see cref="StoreLifetimeKind.Session"/> value belongs to the session the rotation ends, and a
    /// browser value belongs to the device and is never adopted.
    /// </remarks>
    public static async Task<int> AdoptAsync(
        StorageDbContext database,
        string sessionId,
        string account,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        int values = await database.Database.ExecuteSqlAsync($"""
            UPDATE "StoreValues"
            SET "Subject" = {StoreSubjects.ForAccount(account)}
            WHERE "Subject" = {StoreSubjects.ForSession(sessionId)}
              AND "Lifetime" IN ({(int)StoreLifetimeKind.Person}, {(int)StoreLifetimeKind.NextRead});
            """, cancellationToken);

        return values + await StoreMessageSql.AdoptAsync(database, sessionId, account, cancellationToken);
    }
}
