// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One slice of a listing, and whether the store holds more after it.</summary>
/// <param name="Entries">
/// The rows, ordered by key, which is the order <see cref="PageListingRequest.After"/> resumes in. A
/// row is a page, a tombstone, a site route or a folder, and a key may be more than one of them.
/// </param>
/// <param name="Truncated">
/// Whether at least one row stands after the last row. A listing that ends at the last key answers
/// an empty page with this false, which is a complete answer rather than an error.
/// </param>
public sealed record PageListing(IReadOnlyList<PageEntry> Entries, bool Truncated);
