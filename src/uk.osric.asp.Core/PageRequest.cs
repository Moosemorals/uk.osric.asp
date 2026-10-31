// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>The request a page runs against, as plain values.</summary>
/// <remarks>
/// <para>
/// A snapshot taken before the handler runs, so a page cannot observe the request changing under it
/// and the runtime never needs to hand page code an ASP.NET type.
/// </para>
/// <para>
/// The dictionaries hold one value per name: a repeated field keeps its first value. That is all a
/// page needs, and it keeps the contract small.
/// </para>
/// </remarks>
/// <param name="Method">The HTTP method, for example <c>GET</c> or <c>POST</c>.</param>
/// <param name="Path">The canonical page key, which always starts with <c>/</c>.</param>
/// <param name="Query">The query string, collapsed to one value per name.</param>
/// <param name="Form">The submitted form, or empty when the request is not a form post.</param>
/// <param name="Headers">The request headers, collapsed to one value per name.</param>
/// <param name="Cookies">The request cookies.</param>
public sealed record PageRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyDictionary<string, string> Form,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Cookies);
