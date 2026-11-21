// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One slice of the store a page asks for: a subtree, a cursor and a limit.</summary>
/// <param name="Prefix">
/// The subtree to list. <c>""</c> and <c>"/"</c> are the whole store; any other value is normalised
/// as a page key, so <c>/Blog/</c> and <c>/blog</c> are the same request.
/// </param>
/// <param name="After">
/// The last key the caller was given, or <see langword="null"/> to start at the beginning. It is
/// exclusive and normalised as a page key, so <c>/blog/</c> and <c>/blog</c> resume in one place.
/// </param>
/// <param name="Limit">
/// How many rows to answer, or <see langword="null"/> for the host's cap. A limit above the cap is
/// refused rather than trimmed.
/// </param>
/// <param name="Version">
/// The fingerprint a previous answer carried, or <see langword="null"/> for a first call. It is
/// opaque: pass it back unchanged and compare nothing, because the host answers
/// <see cref="PageListing.Changed"/> for you.
/// </param>
public sealed record PageListingRequest(
    string Prefix = "", string? After = null, int? Limit = null, string? Version = null);
