// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One row of a page listing: a key, its head's revision and what kind of key it is.</summary>
/// <param name="Key">The page's canonical key.</param>
/// <param name="SavedAt">When the head's revision was saved.</param>
/// <param name="Hash">The head's revision hash.</param>
/// <param name="IsDeleted">Whether the page is a tombstone, which retains its history.</param>
/// <param name="IsReserved">Whether the site's own routes already answer for the key.</param>
/// <param name="IsHtml">Whether the key is served as HTML.</param>
/// <remarks>
/// <para>
/// The marks are the host's own rules reported rather than derived: a view can filter a listing
/// without owning the reserved-path list, the content-type table or the revision verdict.
/// </para>
/// <para>
/// <see cref="State"/> is what the head's image compiled to, in the host's words — "Compiled",
/// "Compiled with warnings", "Compile failed", "Stale", "Not compiled" or "Stored as source" — so a
/// page's listing says exactly what the shell's own listing says, without reading source or
/// inventing a second spelling. How a view marks it is the view's business: the shell's list has a
/// class of its own, and a page may use the words as an attribute or map them to its own class.
/// </para>
/// </remarks>
public readonly record struct PageEntry(
    string Key,
    DateTimeOffset SavedAt,
    string Hash,
    bool IsDeleted,
    bool IsReserved,
    bool IsHtml) {

    /// <summary>Whether the head has no code, so its source is the response.</summary>
    public bool IsAsset { get; init; }

    /// <summary>What the head's image compiled to, in the host's words.</summary>
    public string State { get; init; } = string.Empty;
}
