// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using uk.osric.asp.Web.Users;

/// <summary>Authenticates the token a run presents to view the site it is serving.</summary>
/// <remarks>
/// <para>
/// The second scheme, and the only one that is not a sign-in: a request carrying
/// <c>Authorization: Bearer</c> is authenticated here or nowhere, and it never falls back to the
/// cookie. The account is validated the way the cookie's is validated on every request — it exists,
/// and its security stamp has not moved — so <c>users passwd</c> in the middle of a run takes the
/// view away.
/// </para>
/// <para>
/// A failed attempt is a failure, never an anonymous result: the caller asserted an identity, and
/// <see cref="ViewGuard"/> turns that into a <c>401</c> rather than a page a stranger would see.
/// </para>
/// </remarks>
internal sealed class ViewAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ViewTokenStore tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {

    private const string Prefix = ViewToken.HeaderScheme + " ";

    /// <inheritdoc/>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        string header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) {
            return AuthenticateResult.NoResult();
        }

        if (!tokens.TryResolve(header[Prefix.Length..].Trim(), out ViewToken? token) || token is null) {
            return AuthenticateResult.Fail("The view token is not one this host minted.");
        }

        LocalUser? user = await Context.RequestServices
            .GetRequiredService<IUserStore>()
            .FindAsync(token.Owner, Context.RequestAborted);

        // The stamp is read when the run starts and compared on every request, so an account that
        // changed its password mid-run stops being visible at the next hop.
        if (user is null || !string.Equals(user.SecurityStamp, token.SecurityStamp, StringComparison.Ordinal)) {
            return AuthenticateResult.Fail("The account behind the view token is gone or has changed.");
        }

        ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, token.Owner),
                new Claim(ViewToken.Claim, token.RunId),
            ],
            ViewToken.AuthenticationScheme,
            ClaimTypes.NameIdentifier,
            ClaimTypes.Role);

        Logger.LogInformation("Run {RunId} viewed {Path} as {Owner}", token.RunId, Request.Path.Value, token.Owner);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
