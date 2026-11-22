// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>How the browser cookie is named, protected and how long it lasts.</summary>
/// <param name="Name">The cookie's name.</param>
/// <param name="SecurePolicy">Whether the cookie is only sent over HTTPS.</param>
/// <param name="Lifetime">How long the cookie lasts from its last write.</param>
internal sealed record PageBrowserCookieOptions(string Name, CookieSecurePolicy SecurePolicy, TimeSpan Lifetime) {

    /// <summary>The browser cookie's name, one character beside the session and auth cookies.</summary>
    public const string DefaultName = ShellCookies.Browser;

    /// <summary>Builds the options for an environment and the configured browser lifetime.</summary>
    /// <param name="environment">The host environment.</param>
    /// <param name="visitor">The visitor store's bounds.</param>
    public static PageBrowserCookieOptions For(IWebHostEnvironment environment, PageVisitorStores visitor) {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(visitor);

        return new PageBrowserCookieOptions(
            DefaultName,
            environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest,
            TimeSpan.FromDays(visitor.BrowserLifetimeDays));
    }
}

/// <summary>Reads, mints, writes and clears the cookie that names one browser.</summary>
/// <remarks>
/// It is not the session id: it survives signing in and out, is never linked to an account, and is
/// minted on the first <c>Browser</c> write so a visitor who never saves a preference costs nothing.
/// Like the session cookie it is <c>HttpOnly</c>, and a page can neither read nor set it.
/// </remarks>
internal static class PageBrowserCookie {

    private const int TokenBytes = 16;
    private const int TokenChars = 22;

    /// <summary>Mints an unguessable browser id.</summary>
    /// <returns>128 random bits as base64url.</returns>
    public static string Mint() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>Reads the browser id the visitor presented.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="options">The cookie's name.</param>
    /// <returns>The id, or <see langword="null"/> when the cookie is absent or malformed.</returns>
    public static string? Read(HttpContext context, PageBrowserCookieOptions options) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        string? value = context.Request.Cookies[options.Name];

        return IsWellFormed(value) ? value : null;
    }

    /// <summary>Writes the browser cookie on a response that is not yet committed.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="browserId">The id to carry.</param>
    /// <param name="options">The cookie's name, secure policy and lifetime.</param>
    public static void Write(HttpContext context, string browserId, PageBrowserCookieOptions options) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(browserId);
        ArgumentNullException.ThrowIfNull(options);

        CookieOptions cookie = CookieOptionsFor(context, options);
        cookie.Expires = DateTimeOffset.UtcNow + options.Lifetime;
        context.Response.Cookies.Append(options.Name, browserId, cookie);
    }

    /// <summary>Deletes the cookie from the visitor's browser.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="options">The cookie's name and secure policy.</param>
    public static void Clear(HttpContext context, PageBrowserCookieOptions options) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        context.Response.Cookies.Delete(options.Name, CookieOptionsFor(context, options));
    }

    private static CookieOptions CookieOptionsFor(HttpContext context, PageBrowserCookieOptions options) {
        return new CookieOptions {
            HttpOnly = true,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = options.SecurePolicy switch {
                CookieSecurePolicy.Always => true,
                CookieSecurePolicy.SameAsRequest => context.Request.IsHttps,
                _ => false,
            },
        };
    }

    // A value is a 22-character base64url token for 16 random bytes. Anything else cannot name a
    // browser this host minted, so it is treated as absent rather than trusted.
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
