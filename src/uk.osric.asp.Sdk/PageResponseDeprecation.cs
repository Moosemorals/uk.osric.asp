// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The one message every member the page contract has replaced carries.</summary>
/// <remarks>
/// It is internal so it is not itself part of the API a page binds against, and it is one constant so
/// the members that carry it cannot drift apart.
/// </remarks>
internal static class PageResponseDeprecation {

    /// <summary>What a page should say instead of the replaced member.</summary>
    internal const string Deprecated =
        "Use IContext.Response and IPageResponse: a page writes into its response now.";
}
