// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One slice of matching lines, the position to resume at, and whether the store moved.</summary>
/// <param name="Matches">The matching lines, in key order and then line order.</param>
/// <param name="Cursor">
/// The first position this answer did not include, or <see langword="null"/> when the search reached the
/// end of its scope. Pass it back as <see cref="PageSearchRequest.After"/> to carry on.
/// </param>
/// <param name="Truncated">
/// Whether the scan stopped before the end of its scope — because <see cref="PageSearchRequest.Limit"/>
/// was reached or the host's time allowance expired — rather than running out of store. It is not an
/// error, and every truncated answer carries a <paramref name="Cursor"/>.
/// </param>
public sealed record PageSearch(IReadOnlyList<PageSearchMatch> Matches, PageSearchCursor? Cursor, bool Truncated) {

    /// <summary>The fingerprint of every head under the prefix, read with this answer.</summary>
    /// <remarks>
    /// It is opaque: compare it with the token a later answer carries, and never parse, order or treat
    /// it as a version. Pass it back as <see cref="PageSearchRequest.Version"/> on the next slice to learn
    /// whether the store moved under the walk. An empty string is only what an answer constructed
    /// without one carries; the host always fills it.
    /// </remarks>
    public string Version { get; init; } = string.Empty;

    /// <summary>Whether the store's fingerprint differs from the one the request carried.</summary>
    /// <remarks>
    /// A warning, not a refusal, and a stronger one than a listing's: a search reads text, so a page
    /// saved under the cursor can hide a match rather than merely move a row. When it is true the host
    /// did not trust the cursor and the scan started again at the beginning, so the caller either
    /// carries on from this answer's <see cref="Cursor"/> or stops.
    /// </remarks>
    public bool Changed { get; init; }
}
