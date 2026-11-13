// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The request a page runs against, as plain values.</summary>
/// <remarks>
/// <para>
/// A snapshot taken before the handler runs, so a page cannot observe the request changing under it
/// and the runtime never needs to hand page code an ASP.NET type.
/// </para>
/// <para>
/// <see cref="Query"/>, <see cref="Form"/> and <see cref="Headers"/> hold one value per name: a
/// repeated field keeps its first value, which is the common case and the name a page already knows.
/// <see cref="QueryValues"/> and <see cref="FormValues"/> hold every value in the order the request
/// sent them, for a multi-select or <c>?tag=a&amp;tag=b</c>.
/// </para>
/// </remarks>
/// <param name="Method">The HTTP method, for example <c>GET</c> or <c>POST</c>.</param>
/// <param name="Path">The canonical page key, which always starts with <c>/</c>.</param>
/// <param name="Query">The query string, collapsed to one value per name.</param>
/// <param name="Form">The submitted form, collapsed to one value per name, or empty when the request is not a form post.</param>
/// <param name="Headers">The request headers, collapsed to one value per name.</param>
/// <param name="Cookies">The request cookies.</param>
/// <param name="QueryValues">Every query value per name, in the order the request sent them.</param>
/// <param name="FormValues">Every form value per name, in the order sent, or empty when the request is not a form post.</param>
public sealed record PageRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyDictionary<string, string> Form,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Cookies,
    IReadOnlyDictionary<string, IReadOnlyList<string>> QueryValues,
    IReadOnlyDictionary<string, IReadOnlyList<string>> FormValues);
