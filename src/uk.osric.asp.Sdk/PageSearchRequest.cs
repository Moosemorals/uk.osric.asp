// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One slice of the store a page asks to have searched.</summary>
/// <param name="Pattern">
/// The text to find, or the pattern to match when <paramref name="Kind"/> says so.
/// </param>
/// <param name="Prefix">
/// The subtree to search. <c>""</c> and <c>"/"</c> are the whole store; any other value is normalised
/// as a page key, so <c>/Blog/</c> and <c>/blog</c> are the same request.
/// </param>
/// <param name="Recursive">
/// Whether to descend. <see langword="true"/>, the default, searches every key under the prefix;
/// <see langword="false"/> searches the prefix itself and its own children, the same pair a listing
/// takes.
/// </param>
/// <param name="Kind">Whether the pattern is literal text or a basic regular expression.</param>
/// <param name="IgnoreCase">Whether matching ignores case. The default is to ignore it, as a search box does.</param>
/// <param name="After">
/// The cursor a previous answer carried, or <see langword="null"/> to start at the beginning. The
/// host ignores it when <paramref name="Version"/> does not match the store, and says so.
/// </param>
/// <param name="Limit">
/// How many matching lines to answer, or <see langword="null"/> for the host's default. A limit above
/// the host's limit is refused rather than trimmed, and the message names the setting to raise.
/// </param>
/// <param name="Context">
/// How wide a snippet of each matching line to answer, in characters, or <see langword="null"/> for
/// the host's default. The snippet is centred on the match and never shorter than the match, so a long
/// match widens it; it never runs past either end of the line. A context above the host's limit is
/// refused rather than trimmed.
/// </param>
/// <param name="Version">
/// The fingerprint a previous answer carried, or <see langword="null"/> for a first call. It is
/// opaque: pass it back unchanged and compare nothing, because the host answers
/// <see cref="PageSearch.Changed"/> for you.
/// </param>
/// <remarks>
/// The defaults are the request: an ignore-case literal over the whole store, descending. The order of
/// the arguments follows <see cref="PageListingRequest"/>'s, so a page that has learned one finds the
/// same words in the same places.
/// </remarks>
public sealed record PageSearchRequest(
    string Pattern,
    string Prefix = "",
    bool Recursive = true,
    PageSearchPatternKind Kind = PageSearchPatternKind.Literal,
    bool IgnoreCase = true,
    PageSearchCursor? After = null,
    int? Limit = null,
    int? Context = null,
    string? Version = null);
