// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Extensions.Primitives;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Http;

/// <summary>Reads the live ASP.NET request once, into the plain values a page sees.</summary>
internal static class PageRequestSnapshot {

    /// <summary>Takes the snapshot a page runs against.</summary>
    /// <param name="request">The request to read.</param>
    /// <returns>The request values; the form is empty when the request is not a form post.</returns>
    public static PageRequest Create(HttpRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<string, string> query = [with(StringComparer.OrdinalIgnoreCase)];
        Dictionary<string, IReadOnlyList<string>> queryValues = [with(StringComparer.OrdinalIgnoreCase)];
        Collect(request.Query, query, queryValues);

        Dictionary<string, string> form = [with(StringComparer.OrdinalIgnoreCase)];
        Dictionary<string, IReadOnlyList<string>> formValues = [with(StringComparer.OrdinalIgnoreCase)];
        if (request.HasFormContentType) {
            Collect(request.Form, form, formValues);
        }

        return new PageRequest(
            request.Method,
            request.Path.Value ?? "/",
            query,
            form,
            SingleValuePerName(request.Headers),
            Cookies(request.Cookies),
            queryValues,
            formValues);
    }

    // One pass builds both shapes: the collapsed value is the first of the ones kept, so a page that
    // only wants the common case and a page that wants a multi-select read the same snapshot.
    private static void Collect(
        IEnumerable<KeyValuePair<string, StringValues>> values,
        Dictionary<string, string> collapsed,
        Dictionary<string, IReadOnlyList<string>> every) {
        foreach (KeyValuePair<string, StringValues> value in values) {
            List<string> all = [.. value.Value.Where(item => item is not null).Select(item => item!)];
            every[value.Key] = all;
            collapsed[value.Key] = all.Count > 0 ? all[0] : string.Empty;
        }
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

    private static string First(StringValues values) {
        foreach (string? value in values) {
            if (value is not null) {
                return value;
            }
        }

        return string.Empty;
    }
}
