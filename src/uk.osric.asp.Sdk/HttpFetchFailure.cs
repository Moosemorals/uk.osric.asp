// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Why a fetch did not produce a usable response.</summary>
/// <remarks>
/// A fetch failure is a value, not an exception, so a page that shows live data can fall back
/// without a <c>try</c>. An HTTP error is a failure even though the remote server answered: the page
/// still gets the real status, so a <c>500</c> is distinguishable from a <c>404</c>.
/// </remarks>
public enum HttpFetchFailure {
    /// <summary>The request completed and the remote server answered.</summary>
    None = 0,

    /// <summary>The URL, method, body, a header or a redirect target was not acceptable. No connection was made to it.</summary>
    InvalidRequest = 1,

    // 2 was NotAllowed. The host allow-list was removed on 2026-11-10 (DEC-216); the value is kept
    // out of the enum so page code cannot branch on a failure the host never reports.

    /// <summary>The per-fetch timeout or the render's deadline fired.</summary>
    Timeout = 3,

    /// <summary>The request was cancelled, or the render was abandoned.</summary>
    Aborted = 4,

    /// <summary>DNS, connect, TLS or protocol failure.</summary>
    Network = 5,

    /// <summary>The remote server answered with a 4xx or 5xx; <see cref="HttpFetchResponse.Status"/> names it.</summary>
    HttpError = 6,

    /// <summary>This render has already made as many requests as it may.</summary>
    TooManyRequests = 7,

    /// <summary>The redirect limit was reached; <see cref="HttpFetchResponse.Status"/> is the last 3xx.</summary>
    TooManyRedirects = 8,
}
