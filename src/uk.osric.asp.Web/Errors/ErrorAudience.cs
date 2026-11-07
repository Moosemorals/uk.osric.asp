// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Errors;

/// <summary>Decides whether a failed request wants a document or a problem document.</summary>
internal static class ErrorAudience {

    /// <summary>Whether a request accepts JSON and does not also prefer HTML.</summary>
    /// <param name="request">The request to read.</param>
    /// <remarks>
    /// A browser sends <c>text/html</c> first and a wildcard last, so HTML wins whenever it is
    /// named. Anything that names only JSON, or a <c>+json</c> type, is talking to a program.
    /// A request that names neither is treated as a browser: a person is the more likely caller,
    /// and a human who gets a document can still read it, where a program given HTML cannot.
    /// </remarks>
    public static bool PrefersJson(HttpRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        string accept = request.Headers.Accept.ToString();
        if (accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return accept.Contains("json", StringComparison.OrdinalIgnoreCase);
    }
}
