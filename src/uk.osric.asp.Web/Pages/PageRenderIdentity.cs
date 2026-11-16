// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>One render of one page in a chain: its key and the query it runs against.</summary>
/// <remarks>
/// The ancestor stack is keyed by this rather than by the key alone, because a page that embeds
/// itself with a different query is a recursive component rather than a cycle. The same key with the
/// same query is the same render, which is the one thing a chain may never contain twice.
/// </remarks>
/// <param name="Key">The page's canonical key.</param>
/// <param name="Query">The fingerprint of the request's query.</param>
internal readonly record struct PageRenderIdentity(string Key, string Query) {

    /// <summary>The identity a page has when it runs against a request.</summary>
    /// <param name="key">The page's canonical key.</param>
    /// <param name="request">The request it runs against.</param>
    public static PageRenderIdentity Of(PagePath key, PageRequest request) =>
        new(key.Key, request.QueryValues.Fingerprint());
}
