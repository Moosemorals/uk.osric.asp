// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One key of one page's state inside one visitor's session.</summary>
/// <remarks>
/// The page path is required on every row, including the shell's own keys. There is no null or
/// empty area, which is also what keeps SQLite's NULL-is-distinct rule out of the unique key.
/// </remarks>
internal sealed class PageSessionValueEntry {
    public required string SessionId { get; init; }

    public required string PagePath { get; init; }

    public required string Key { get; init; }

    /// <summary>The value as JSON. A stored <c>null</c> is the text <c>null</c>, not a missing row.</summary>
    public required string Value { get; set; }
}
