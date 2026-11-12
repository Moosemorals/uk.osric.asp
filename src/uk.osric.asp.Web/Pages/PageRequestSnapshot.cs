// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Extensions.Primitives;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;

/// <summary>Reads the live ASP.NET request once, into the plain values a page sees.</summary>
internal static class PageRequestSnapshot {

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    /// <summary>Takes the snapshot a page runs against.</summary>
    /// <param name="request">The request to read.</param>
    /// <returns>The request values; the form is empty when the request is not a form post.</returns>
    public static PageRequest Create(HttpRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        return new PageRequest(
            request.Method,
            request.Path.Value ?? "/",
            SingleValuePerName(request.Query),
            request.HasFormContentType ? SingleValuePerName(request.Form) : Empty,
            SingleValuePerName(request.Headers),
            Cookies(request.Cookies));
    }

    private static Dictionary<string, string> SingleValuePerName(IEnumerable<KeyValuePair<string, StringValues>> values) {
        Dictionary<string, string> snapshot = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (KeyValuePair<string, StringValues> value in values) {
            snapshot[value.Key] = First(value.Value);
        }

        return snapshot;
    }

    // The shell's own cookies are not the page's to read: the session id is a bearer value for one
    // visitor's state, and the ticket key and antiforgery pair are the shell's identity. A page that
    // echoed one into a response would hand another visitor its state.
    private static Dictionary<string, string> Cookies(IRequestCookieCollection cookies) {
        Dictionary<string, string> snapshot = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (KeyValuePair<string, string> cookie in cookies) {
            if (!ShellCookies.IsOwned(cookie.Key)) {
                snapshot[cookie.Key] = cookie.Value;
            }
        }

        return snapshot;
    }

    // A repeated field keeps its first value: the contract is one value per name.
    private static string First(StringValues values) {
        foreach (string? value in values) {
            if (value is not null) {
                return value;
            }
        }

        return string.Empty;
    }
}
