// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The secret statements a caller with its own transaction needs.</summary>
/// <remarks>
/// The write proves every rule in the one statement that stores: the subject's name count, its byte
/// total and, for a page write, that the page still has a live head. A page's delete and move own a
/// transaction, and the store's seam opens its own context, so these statements live here for the
/// callers to run inside theirs.
/// </remarks>
internal static class StoreSecretSql {

    /// <summary>Writes one secret under its subject's limits, in one statement.</summary>
    /// <param name="database">The context whose transaction the write belongs to.</param>
    /// <param name="write">The row to write.</param>
    /// <param name="updatedAt">The fixed-width timestamp to store.</param>
    /// <param name="limits">The limits the subject obeys.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were written: 1 on success, 0 when a limit or a missing live page refused it.</returns>
    public static Task<int> UpsertAsync(
        StorageDbContext database,
        StoreSecretWrite write,
        string updatedAt,
        PageSecretOptions limits,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(limits);

        long valueBytes = Encoding.UTF8.GetByteCount(write.Value);
        long added = Encoding.UTF8.GetByteCount(write.Name) + valueBytes;

        // Replacing the same name does not make the subject any larger, so the row being replaced is
        // left out of both totals. `Name` and `Value` are the store's own columns, written here.
        return database.Database.ExecuteSqlAsync($"""
            INSERT INTO "StoreSecrets" ("Subject", "PageKey", "Name", "Value", "UpdatedAt")
            SELECT {write.Subject}, {write.PageKey}, {write.Name}, {write.Value}, {updatedAt}
            WHERE ({(write.RequireLivePage ? 1 : 0)} = 0
                    OR EXISTS (SELECT 1 FROM "PageHeads" WHERE "Path" = {write.PageKey} AND "IsDeleted" = 0))
              AND (EXISTS (
                    SELECT 1 FROM "StoreSecrets"
                    WHERE "Subject" = {write.Subject} AND "Name" = {write.Name})
                OR (SELECT COUNT(*) FROM "StoreSecrets" WHERE "Subject" = {write.Subject}) < {limits.MaxSecrets})
              AND {valueBytes} <= {limits.MaxSecretBytes}
              AND (SELECT COALESCE(SUM(length(CAST("Name" AS BLOB)) + length(CAST("Value" AS BLOB))), 0)
                    FROM "StoreSecrets"
                    WHERE "Subject" = {write.Subject} AND "Name" <> {write.Name}) + {added} <= {limits.MaxSubjectBytes}
            ON CONFLICT ("Subject", "PageKey", "Name") DO UPDATE SET
                "Value" = excluded."Value",
                "UpdatedAt" = excluded."UpdatedAt"
            WHERE length(CAST(excluded."Value" AS BLOB)) <= {limits.MaxSecretBytes}
              AND (SELECT COALESCE(SUM(length(CAST("Name" AS BLOB)) + length(CAST("Value" AS BLOB))), 0)
                    FROM "StoreSecrets"
                    WHERE "Subject" = excluded."Subject" AND "Name" <> excluded."Name")
                  + length(CAST(excluded."Name" AS BLOB)) + length(CAST(excluded."Value" AS BLOB)) <= {limits.MaxSubjectBytes};
            """, cancellationToken);
    }

    /// <summary>Removes every secret a page holds.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="key">The page's canonical key.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many rows were removed.</returns>
    public static Task<int> DeletePageAsync(StorageDbContext database, PagePath key, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(key);

        return DeleteSubjectAsync(database, StoreSubjectKeys.ForPage(key), cancellationToken);
    }

    /// <summary>Re-keys a page's secrets to a new key.</summary>
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
            UPDATE "StoreSecrets"
            SET "Subject" = {StoreSubjectKeys.ForPage(to)}, "PageKey" = {to.Key}
            WHERE "Subject" = {StoreSubjectKeys.ForPage(from)};
            """, cancellationToken);
    }

    /// <summary>Removes every secret one subject holds.</summary>
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
            $"""DELETE FROM "StoreSecrets" WHERE "Subject" = {subject};""", cancellationToken);
    }
}
