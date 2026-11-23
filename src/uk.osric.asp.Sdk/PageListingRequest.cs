// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One slice of the store a page asks for: a subtree, a depth, a cursor and a limit.</summary>
/// <param name="Prefix">
/// The subtree to list. <c>""</c> and <c>"/"</c> are the whole store; any other value is normalised
/// as a page key, so <c>/Blog/</c> and <c>/blog</c> are the same request.
/// </param>
/// <param name="After">
/// The last key the caller was given, or <see langword="null"/> to start at the beginning. It is
/// exclusive and normalised as a page key, so <c>/blog/</c> and <c>/blog</c> resume in one place. A
/// folder or a route row is a key like any other.
/// </param>
/// <param name="Limit">
/// How many rows to answer, or <see langword="null"/> for the host's limit. A limit above the limit is
/// refused rather than trimmed, and folder and route rows count towards it.
/// </param>
/// <param name="Version">
/// The fingerprint a previous answer carried, or <see langword="null"/> for a first call. It is
/// opaque: pass it back unchanged and compare nothing, because the host answers
/// <see cref="PageListing.Changed"/> for you.
/// </param>
/// <param name="Recursive">
/// Whether to descend. <see langword="true"/>, the default, answers every key under the prefix;
/// <see langword="false"/> answers the prefix itself and its own children, so a tree that descends
/// one folder per request asks for one level at a time.
/// </param>
/// <remarks>
/// <see cref="Version"/> comes before <see cref="Recursive"/> because it was there first: a caller
/// that passes four arguments means the token, and a depth is named.
/// </remarks>
public sealed record PageListingRequest(
    string Prefix = "", string? After = null, int? Limit = null, string? Version = null, bool Recursive = true);
