// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One visitor's session: the identity, its account and when it last moved.</summary>
/// <remarks>
/// The session id is the value of the shell's cookie and is never chosen by a page. <see cref="Account"/>
/// is the normalised user name the session belongs to, or <see langword="null"/> for an anonymous
/// visitor.
/// </remarks>
internal sealed class PageSessionEntry {
    public required string SessionId { get; init; }

    /// <summary>The normalised account name, or <see langword="null"/> for an anonymous visitor.</summary>
    public string? Account { get; init; }

    /// <summary>When the row was minted, in UTC.</summary>
    /// <remarks>
    /// A <see cref="DateTime"/> rather than a <see cref="DateTimeOffset"/> because SQLite cannot
    /// order by an offset and eviction is an order: every timestamp this store writes is UTC.
    /// </remarks>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the row was last read or written, in UTC. Reading slides the idle timer.</summary>
    public required DateTime UpdatedAt { get; set; }
}
