// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using System.Security.Claims;

/// <summary>The one place a request's owner id is read from its principal.</summary>
/// <remarks>
/// The id is the account name the sign-in ticket was minted for, and every owner-scoped store keys
/// its rows by it. Reading it in one place is what keeps a second spelling of the claim — or a second
/// idea of what "signed in" means — out of the shell.
/// </remarks>
internal static class SignedInOwner {

    /// <summary>The account the request is signed in as.</summary>
    /// <param name="context">The request being answered.</param>
    /// <returns>The owner id, or null when nothing is signed in.</returns>
    public static string? Of(HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);

        return context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
