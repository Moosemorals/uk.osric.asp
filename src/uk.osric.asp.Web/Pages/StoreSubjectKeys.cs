// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Globalization;
using uk.osric.asp.Core.Pages;

/// <summary>The four subject prefixes, and the one place a subject string is built.</summary>
/// <remarks>
/// A subject is the binding a handle is written to. The prefix is not part of a page's reach — a page
/// never sees a subject key — but keeping the spelling in one place keeps a read, a write, an adoption
/// and a sweep naming the same row. The prefixes cannot collide: a page key starts with <c>/</c> and a
/// session or browser id is base64url.
/// </remarks>
internal static class StoreSubjectKeys {

    public const string PagePrefix = "page:";

    public const string AccountPrefix = "account:";

    public const string SessionPrefix = "session:";

    public const string BrowserPrefix = "browser:";

    /// <summary>The subject a page's own data lives under.</summary>
    public static string ForPage(PagePath key) {
        ArgumentNullException.ThrowIfNull(key);

        return PagePrefix + key.Key;
    }

    /// <summary>The subject an account's person data lives under.</summary>
    public static string ForAccount(string account) {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        return AccountPrefix + account;
    }

    /// <summary>The subject an anonymous or signed-in session's session data lives under.</summary>
    public static string ForSession(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return SessionPrefix + sessionId;
    }

    /// <summary>The subject a browser's device data lives under.</summary>
    public static string ForBrowser(string browserId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(browserId);

        return BrowserPrefix + browserId;
    }

    /// <summary>Whether a subject is a page's.</summary>
    public static bool IsPage(string subject) => subject.StartsWith(PagePrefix, StringComparison.Ordinal);

    /// <summary>Whether a subject is an account's.</summary>
    public static bool IsAccount(string subject) => subject.StartsWith(AccountPrefix, StringComparison.Ordinal);

    /// <summary>Whether a subject is a session's.</summary>
    public static bool IsSession(string subject) => subject.StartsWith(SessionPrefix, StringComparison.Ordinal);

    /// <summary>Whether a subject is a browser's.</summary>
    public static bool IsBrowser(string subject) => subject.StartsWith(BrowserPrefix, StringComparison.Ordinal);

    /// <summary>A fixed-width UTC timestamp, which is what makes an expiry a plain text comparison.</summary>
    public static string Stamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The longest a stored subject may be, given the configured key length.</summary>
    public static int MaxLength(int maxKeyChars) => Math.Max(maxKeyChars, 32) + BrowserPrefix.Length;
}
