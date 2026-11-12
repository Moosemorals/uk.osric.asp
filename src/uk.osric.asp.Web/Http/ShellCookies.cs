// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Http;

/// <summary>The cookies the shell owns, and the one place their names are written.</summary>
/// <remarks>
/// The names are one character each because a browser's cookie pane stops being self-explanatory.
/// They are defined here so the authentication registration, the page session and the request
/// snapshot a page sees cannot drift: a page is never handed one of these, and never sets one.
/// </remarks>
internal static class ShellCookies {

    /// <summary>The authentication ticket's key.</summary>
    public const string Authentication = "a";

    /// <summary>The antiforgery pair's cookie.</summary>
    public const string Antiforgery = "f";

    /// <summary>The page session's id.</summary>
    public const string Session = "s";

    /// <summary>Whether <paramref name="name"/> belongs to the shell rather than to a page.</summary>
    /// <param name="name">The cookie name to test.</param>
    /// <returns>True when the shell owns the cookie.</returns>
    public static bool IsOwned(string name) =>
        string.Equals(name, Authentication, StringComparison.Ordinal)
            || string.Equals(name, Antiforgery, StringComparison.Ordinal)
            || string.Equals(name, Session, StringComparison.Ordinal);
}
