// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Claims;
using System.Security.Cryptography;

/// <summary>Puts a random key in the cookie and keeps the protected ticket in SQLite.</summary>
/// <remarks>
/// The handler never sees this difference: <see cref="Protect(AuthenticationTicket)"/> still returns the cookie value and
/// <see cref="Unprotect(string?)"/> still returns the ticket. The inner format is the framework's own, so
/// serialisation and the data protection purposes stay exactly as they were.
/// </remarks>
/// <param name="inner">The framework format whose output is stored instead of sent.</param>
/// <param name="tickets">The store that holds the protected ticket.</param>
/// <param name="http">The current request, so a renewal can keep its key.</param>
/// <param name="cookieName">The cookie the handler will set, and read to find the current key.</param>
internal sealed class StoredTicketDataFormat(
    ISecureDataFormat<AuthenticationTicket> inner,
    AuthTicketStore tickets,
    IHttpContextAccessor http,
    string cookieName) : ISecureDataFormat<AuthenticationTicket> {

    // 128 bits, as much as a session identifier needs, base64url-encoded into 22 characters.
    private const int KeyBytes = 16;

    // A request may be both authenticating and signing in; only the endpoint knows that.
    private static readonly object s_signInMarker = new();

    /// <summary>Marks the request as an explicit sign-in, which always gets a new cookie key.</summary>
    /// <param name="context">The request that is about to sign in.</param>
    /// <remarks>
    /// A sign-in must never keep a key the browser already had: a cookie planted for the origin
    /// would otherwise inherit the new session. Sliding renewal, which is not marked, keeps its key.
    /// </remarks>
    public static void MarkSignIn(HttpContext context) {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[s_signInMarker] = true;
    }

    /// <inheritdoc/>
    public string Protect(AuthenticationTicket data) => Protect(data, null);

    /// <inheritdoc/>
    public string Protect(AuthenticationTicket data, string? purpose) {
        ArgumentNullException.ThrowIfNull(data);

        string protectedTicket = inner.Protect(data, purpose);
        DateTime expiresAt = data.Properties.ExpiresUtc?.UtcDateTime
            ?? throw new InvalidOperationException("A stored authentication ticket must carry an expiry.");
        string userName = data.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("A stored authentication ticket must name its account.");

        string? current = http.HttpContext?.Request.Cookies[cookieName];
        if (!string.IsNullOrEmpty(current)) {
            if (IsSignIn(http.HttpContext)) {
                // The session that key belonged to is over; its row goes with it.
                _ = tickets.Remove(current);
            } else {
                AuthTicket? existing = tickets.Find(current);
                if (existing is not null
                    && string.Equals(existing.UserName, userName, StringComparison.Ordinal)
                    && tickets.Update(current, userName, protectedTicket, expiresAt)) {
                    return current;
                }

                // A renewal whose session was revoked (sign-out or another sign-in) while this
                // request ran. Hand back the dead key instead of minting a live replacement: the
                // next request fails to unprotect, so the browser is asked to sign in again.
                return current;
            }
        }

        string key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(KeyBytes));
        tickets.Add(new AuthTicket {
            Key = key,
            UserName = userName,
            ProtectedTicket = protectedTicket,
            ExpiresAt = expiresAt,
        });

        return key;
    }

    /// <inheritdoc/>
    public AuthenticationTicket? Unprotect(string? protectedText) => Unprotect(protectedText, null);

    /// <inheritdoc/>
    public AuthenticationTicket? Unprotect(string? protectedText, string? purpose) {
        if (string.IsNullOrEmpty(protectedText)) {
            return null;
        }

        AuthTicket? ticket = tickets.Find(protectedText);
        return ticket is null ? null : inner.Unprotect(ticket.ProtectedTicket, purpose);
    }

    private static bool IsSignIn(HttpContext? context) {
        return context?.Items.ContainsKey(s_signInMarker) == true;
    }
}
