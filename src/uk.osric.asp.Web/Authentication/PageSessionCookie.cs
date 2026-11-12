// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>How the page-session cookie is named and protected.</summary>
/// <param name="Name">The cookie's name.</param>
/// <param name="SecurePolicy">Whether the cookie is only sent over HTTPS.</param>
internal sealed record PageSessionCookieOptions(string Name, CookieSecurePolicy SecurePolicy) {

    /// <summary>The session cookie's name, one character beside the auth and antiforgery cookies.</summary>
    public const string DefaultName = ShellCookies.Session;

    /// <summary>Builds the options for an environment, using the same secure policy as the other cookies.</summary>
    /// <param name="environment">The host environment.</param>
    public static PageSessionCookieOptions For(IWebHostEnvironment environment) {
        ArgumentNullException.ThrowIfNull(environment);

        return new PageSessionCookieOptions(
            DefaultName,
            environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest);
    }
}

/// <summary>Reads, mints, writes and clears the cookie that names one visitor's session.</summary>
/// <remarks>
/// A page cannot read or set this cookie: <c>Set-Cookie</c> is refused by <c>SimpleContext</c>'s
/// allow-list and <c>HttpOnly</c> keeps it from script. The value is 128 random bits in base64url,
/// so a visitor cannot guess another's session, and this store never accepts a value it did not
/// mint.
/// </remarks>
internal static class PageSessionCookie {

    private const int TokenBytes = 16;
    private const int TokenChars = 22;

    /// <summary>Mints an unguessable session id.</summary>
    /// <returns>128 random bits as base64url.</returns>
    public static string Mint() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>Reads the session id the visitor presented.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="options">The cookie's name.</param>
    /// <returns>The id, or <see langword="null"/> when the cookie is absent or malformed.</returns>
    public static string? Read(HttpContext context, PageSessionCookieOptions options) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        string? value = context.Request.Cookies[options.Name];

        return IsWellFormed(value) ? value : null;
    }

    /// <summary>Writes the session cookie on a response that is not yet committed.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="sessionId">The id to carry.</param>
    /// <param name="options">The cookie's name and secure policy.</param>
    public static void Write(HttpContext context, string sessionId, PageSessionCookieOptions options) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(options);

        context.Response.Cookies.Append(options.Name, sessionId, CookieOptionsFor(context, options));
    }

    /// <summary>Deletes the cookie from the visitor's browser.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="options">The cookie's name and secure policy.</param>
    public static void Clear(HttpContext context, PageSessionCookieOptions options) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        context.Response.Cookies.Delete(options.Name, CookieOptionsFor(context, options));
    }

    /// <summary>Discards the visitor's session and mints one that belongs to an account.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="store">Where the session row lives.</param>
    /// <param name="options">The cookie's name and secure policy.</param>
    /// <param name="account">The normalised account name that now owns the session.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether the new session was minted and its cookie written.</returns>
    /// <remarks>
    /// A sign-in rotates rather than adopts: the pre-existing row is deleted so the account cannot
    /// inherit what an anonymous visitor (or a previous account) left behind. A site with no room
    /// for another session leaves the visitor signed in without per-visitor state.
    /// </remarks>
    public static async Task<bool> RotateAsync(
        HttpContext context,
        IPageSessionStore store,
        PageSessionCookieOptions options,
        string account,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        if (Read(context, options) is { } previous) {
            _ = await store.DeleteAsync(previous, cancellationToken);
        }

        string sessionId = Mint();
        if (!await store.EnsureAsync(sessionId, account, cancellationToken)) {
            return false;
        }

        Write(context, sessionId, options);
        return true;
    }

    /// <summary>Deletes the visitor's session, if the request presented one.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="store">Where the session row lives.</param>
    /// <param name="options">The cookie's name and secure policy.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public static async Task ClearAsync(
        HttpContext context,
        IPageSessionStore store,
        PageSessionCookieOptions options,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        if (Read(context, options) is { } sessionId) {
            _ = await store.DeleteAsync(sessionId, cancellationToken);
        }

        Clear(context, options);
    }

    private static CookieOptions CookieOptionsFor(HttpContext context, PageSessionCookieOptions options) {
        return new CookieOptions {
            HttpOnly = true,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Strict,
            Secure = options.SecurePolicy switch {
                CookieSecurePolicy.Always => true,
                CookieSecurePolicy.SameAsRequest => context.Request.IsHttps,
                _ => false,
            },
        };
    }

    // A value is a 22-character base64url token for 16 random bytes. Anything else cannot name a
    // session this host minted, so it is treated as absent rather than looked up.
    private static bool IsWellFormed(string? value) {
        if (value is null || value.Length != TokenChars) {
            return false;
        }

        try {
            return WebEncoders.Base64UrlDecode(value) is { Length: TokenBytes };
        } catch (FormatException) {
            return false;
        }
    }
}
