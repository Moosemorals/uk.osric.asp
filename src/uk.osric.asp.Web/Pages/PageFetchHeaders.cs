// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Collections.Frozen;

/// <summary>The header names a page may send and see.</summary>
/// <remarks>
/// Both lists are allow-lists rather than filters. Framing headers, <c>Host</c>, <c>Cookie</c> and
/// <c>Set-Cookie</c> are refused outright, so nothing ambient is forwarded and nothing ambient comes
/// back; a page that means to send a credential does it through <c>Authorization</c> on purpose.
/// </remarks>
internal static class PageFetchHeaders {

    private const string CustomPrefix = "X-";

    private static readonly FrozenSet<string> s_request = new[] {
        "Accept",
        "Accept-Charset",
        "Accept-Encoding",
        "Accept-Language",
        "Authorization",
        "Cache-Control",
        "Content-Type",
        "If-Modified-Since",
        "If-None-Match",
        "Range",
        "User-Agent",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> s_response = new[] {
        "Cache-Control",
        "Content-Language",
        "Content-Type",
        "ETag",
        "Expires",
        "Last-Modified",
        "Location",
        "Retry-After",
        "Vary",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a page may send a header with this name.</summary>
    /// <param name="name">The header name.</param>
    /// <returns><see langword="true"/> for a listed name or an <c>X-</c> name.</returns>
    public static bool IsRequestAllowed(string name) {
        ArgumentNullException.ThrowIfNull(name);

        return s_request.Contains(name) || name.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a response header is returned to the page.</summary>
    /// <param name="name">The header name.</param>
    /// <returns><see langword="true"/> for a listed name or an <c>X-</c> name.</returns>
    public static bool IsResponseAllowed(string name) {
        ArgumentNullException.ThrowIfNull(name);

        return s_response.Contains(name) || name.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
