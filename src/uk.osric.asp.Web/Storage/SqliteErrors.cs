// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>The SQLite failures a store treats as an ordinary race rather than a fault.</summary>
internal static class SqliteErrors {

    /// <summary>How many times a write that may lose a race on a unique value is attempted.</summary>
    public const int RetryCount = 3;

    /// <summary>Whether a failed update lost a race on a unique value, so a fresh value may succeed.</summary>
    /// <param name="exception">The failure a save threw.</param>
    public static bool IsUniqueViolation(DbUpdateException exception) {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException is SqliteException {
            SqliteExtendedErrorCode: 1555 or 2067,
        };
    }
}
