// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Storage;

/// <summary>The message statements a caller with its own transaction needs.</summary>
/// <remarks>
/// A page's delete and move and a session's end each own a transaction, and the store's seam opens
/// its own context, so these statements live here for the callers to run inside theirs.
/// </remarks>
internal static class StoreMessageSql {

    /// <summary>Removes every message addressed to a page, whoever owns it.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="key">The page's canonical key.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many messages were removed.</returns>
    public static Task<int> DeletePageAsync(StorageDbContext database, PagePath key, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(key);

        return database.Database.ExecuteSqlAsync(
            $"""DELETE FROM "StoreMessages" WHERE "ToPageKey" = {key.Key};""", cancellationToken);
    }

    /// <summary>Re-keys the messages addressed to a moved page.</summary>
    /// <param name="database">The context whose transaction the move belongs to.</param>
    /// <param name="from">The key the page has.</param>
    /// <param name="to">The key it should have.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many messages were re-keyed.</returns>
    public static Task<int> MovePageAsync(
        StorageDbContext database,
        PagePath from,
        PagePath to,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return database.Database.ExecuteSqlAsync(
            $"""UPDATE "StoreMessages" SET "ToPageKey" = {to.Key} WHERE "ToPageKey" = {from.Key};""",
            cancellationToken);
    }

    /// <summary>Removes every message one subject holds.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="subject">The subject string.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many messages were removed.</returns>
    public static Task<int> DeleteSubjectAsync(
        StorageDbContext database,
        string subject,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        return database.Database.ExecuteSqlAsync(
            $"""DELETE FROM "StoreMessages" WHERE "Subject" = {subject};""", cancellationToken);
    }

    /// <summary>Removes every message held by a set of sessions.</summary>
    /// <param name="database">The context whose transaction the delete belongs to.</param>
    /// <param name="subjects">The subjects that are ending.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many messages were removed.</returns>
    public static Task<int> DeleteSubjectsAsync(
        StorageDbContext database,
        IReadOnlyList<string> subjects,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(subjects);

        return subjects.Count == 0
            ? Task.FromResult(0)
            : database.Set<StoreMessageEntry>()
                .Where(row => subjects.Contains(row.Subject))
                .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Re-keys an anonymous session's person messages into the account.</summary>
    /// <param name="database">The context whose transaction the adoption belongs to.</param>
    /// <param name="sessionId">The session the visitor is leaving.</param>
    /// <param name="account">The normalised account name being signed in to.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>How many messages were adopted.</returns>
    /// <remarks>
    /// Only <see cref="StoreLifetimeKind.Person"/> moves: a session message belongs to the visit the
    /// rotation ends, and a browser message belongs to the device and is never adopted.
    /// </remarks>
    public static Task<int> AdoptAsync(
        StorageDbContext database,
        string sessionId,
        string account,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        return database.Database.ExecuteSqlAsync($"""
            UPDATE "StoreMessages"
            SET "Subject" = {StoreSubjectKeys.ForAccount(account)}
            WHERE "Subject" = {StoreSubjectKeys.ForSession(sessionId)}
              AND "Lifetime" = {(int)StoreLifetimeKind.Person};
            """, cancellationToken);
    }
}
