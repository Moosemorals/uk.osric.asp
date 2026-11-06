// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One key of one page's state, with its value stored as JSON.</summary>
/// <remarks>
/// There is no foreign key to <see cref="PageHead"/>: SQLite's enforcement depends on the connection,
/// and the deletes that keep state from outliving a page are explicit anyway. Keeping the
/// relationship out of the schema avoids a migration-order dependency for no gain.
/// </remarks>
internal sealed class PageStateEntry {
    public required string PagePath { get; init; }

    public required string Key { get; init; }

    /// <summary>The value as JSON. A stored <c>null</c> is the text <c>null</c>, not a missing row.</summary>
    public required string Value { get; set; }

    /// <summary>When the row was last written; informational, and taken from the registered clock.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
