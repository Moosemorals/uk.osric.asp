// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One slice of a listing, whether the store holds more after it, and whether the list moved.</summary>
/// <param name="Entries">The rows, ordered by key, which is the order <see cref="PageListingRequest.After"/> resumes in.</param>
/// <param name="Truncated">
/// Whether at least one head stands after the last row. A listing that ends at the last key answers
/// an empty page with this false, which is a complete answer rather than an error.
/// </param>
public sealed record PageListing(IReadOnlyList<PageEntry> Entries, bool Truncated) {

    /// <summary>The fingerprint of every head under the prefix, read with these rows.</summary>
    /// <remarks>
    /// It is opaque: compare it with the token a later answer carries, and never parse, order or
    /// treat it as a version. Pass it back as <see cref="PageListingRequest.Version"/> on the next
    /// slice to learn whether the set of rows changed. An empty string is only what a listing
    /// constructed without one carries; the host always fills it.
    /// </remarks>
    public string Version { get; init; } = string.Empty;

    /// <summary>Whether the store's fingerprint differs from the one the request carried.</summary>
    /// <remarks>
    /// A warning, not a refusal: the rows are still the keyset the cursor says, and the caller decides
    /// whether to carry on, start again or ignore it. A first call carries no token and is never
    /// changed. It covers the head rows, not what they compiled to, so a lazy compile or a deploy
    /// can change a row's state without moving it.
    /// </remarks>
    public bool Changed { get; init; }
}
