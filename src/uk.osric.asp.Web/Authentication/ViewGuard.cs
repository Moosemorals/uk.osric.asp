// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using uk.osric.asp.Web.Http;

/// <summary>The two rules a view token is held to, in one place, before anything else sees the request.</summary>
/// <remarks>
/// <para>
/// A header that did not authenticate is a <c>401</c>, never a silent anonymous view: the caller
/// asserted an identity, and a page that quietly answers as a stranger is how "why is my page showing
/// the sign-in form" becomes an afternoon.
/// </para>
/// <para>
/// A view reads and never writes. The check is here rather than on the page endpoint because the page
/// endpoint covers pages only — the editor, the admin tools, the assistant and the memory page are all
/// shell routes with their own <c>POST</c>s — and a rule that has to be repeated by the next endpoint
/// is a rule that will be forgotten by one. It runs before authorisation and long before the
/// antiforgery check, so a token can never reach a form that changes state.
/// </para>
/// </remarks>
internal static class ViewGuard {

    private const string Prefix = ViewToken.HeaderScheme + " ";

    /// <summary>Refuses a view that did not authenticate, and one that tries to write.</summary>
    /// <param name="context">The request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    public static async Task ApplyAsync(HttpContext context, RequestDelegate next) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        string header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) {
            await next(context);

            return;
        }

        if (context.User.Identity?.IsAuthenticated != true) {
            SecurityHeaders.NoStore(context);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = ViewToken.HeaderScheme;

            return;
        }

        // Authenticated by another scheme while presenting a bearer header is not a view: the cookie
        // is a sign-in and keeps every method it had.
        if (!context.User.HasClaim(claim => string.Equals(claim.Type, ViewToken.Claim, StringComparison.Ordinal))) {
            await next(context);

            return;
        }

        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) {
            await next(context);

            return;
        }

        SecurityHeaders.NoStore(context);
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = "GET, HEAD";
    }
}
