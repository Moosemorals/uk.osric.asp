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
/// The marks are the host's own rules reported rather than derived: a view can filter a listing
/// without owning the reserved-path list, the content-type table or the revision verdict.
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
}
