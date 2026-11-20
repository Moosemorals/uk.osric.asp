// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.EntityFrameworkCore;

/// <summary>Puts the store in WAL, and refuses to run anywhere else.</summary>
/// <remarks>
/// <para>
/// In rollback-journal mode a reader holding a transaction blocks a writer's commit for the busy
/// timeout, so a page reading the store could fail a page writing it. WAL makes a reader and the one
/// writer independent, which is what lets a commit stay short without depending on what every other
/// request is doing.
/// </para>
/// <para>
/// The mode is a property of the database file, so this happens once per database and a restart finds
/// it already set. A filesystem that cannot do WAL answers something else, and the site stops rather
/// than serving with a weaker guarantee than the one it records: a loud failure at startup is better
/// than a commit that mysteriously waits the busy timeout out.
/// </para>
/// </remarks>
internal static class SqliteJournalMode {

    /// <summary>Switches the store to WAL, or throws.</summary>
    /// <param name="database">The context whose connection is checked.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="InvalidOperationException">SQLite did not put the database in WAL mode.</exception>
    public static async Task EnsureWriteAheadLogAsync(StorageDbContext database, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(database);

        await database.Database.OpenConnectionAsync(cancellationToken);
        try {
            // A pragma is not composable SQL, so it is executed and then read back through the
            // table-valued function of the same name, with the column EF Core maps a scalar from.
            await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL", cancellationToken);

            string? mode = await database.Database
                .SqlQueryRaw<string>("""SELECT journal_mode AS "Value" FROM pragma_journal_mode""")
                .SingleAsync(cancellationToken);

            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"The page store could not be put in WAL mode: SQLite answered '{mode}'. WAL needs a local filesystem that supports shared memory; a network filesystem cannot hold the store.");
            }
        } finally {
            await database.Database.CloseConnectionAsync();
        }
    }
}
