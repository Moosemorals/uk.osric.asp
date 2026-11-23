// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Security.Claims;
using uk.osric.asp.Sdk;

/// <summary>The signed-in account, as the page contract sees it, from the authenticated principal.</summary>
/// <remarks>
/// One mapping for the page endpoint and the shell's own routes, so a half-built principal fails closed
/// everywhere rather than in one place only.
/// </remarks>
internal static class PageUserMapping {

    /// <summary>Maps an authenticated principal to the account a page is running for.</summary>
    /// <param name="principal">The principal the authentication handler validated.</param>
    /// <returns>The account, or <see langword="null"/> when the request is anonymous or nameless.</returns>
    public static PageUser? From(ClaimsPrincipal principal) {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true) {
            return null;
        }

        string? name = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrEmpty(name) ? null : new PageUser(name);
    }
}
