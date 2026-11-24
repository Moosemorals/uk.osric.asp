// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The SQL functions EF Core does not map, translated to SQLite's own.</summary>
/// <remarks>
/// A member here is never called in C#: <see cref="Storage.StorageDbContext"/> names the SQL function
/// it stands for, and a query that uses one is translated. <c>octet_length</c> answers a text value's
/// UTF-8 byte count from the record header, which is why a listing can carry a page's size without
/// reading its source.
/// </remarks>
internal static class SqliteTextFunctions {

    /// <summary>The UTF-8 byte count of a text value, as SQLite's <c>octet_length</c> answers it.</summary>
    /// <param name="value">The text to measure.</param>
    /// <returns>The byte count.</returns>
    public static int? OctetLength(string? value) =>
        throw new NotSupportedException("octet_length is translated to SQL and never run in C#.");
}
