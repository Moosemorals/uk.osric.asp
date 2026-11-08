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
    None,

    /// <summary>The URL, method, body or a header was not acceptable. No connection was made.</summary>
    InvalidRequest,

    /// <summary>The host is not on the operator's allow-list. No connection was made.</summary>
    NotAllowed,

    /// <summary>The per-fetch timeout or the render's deadline fired.</summary>
    Timeout,

    /// <summary>The request was cancelled, or the render was abandoned.</summary>
    Aborted,

    /// <summary>DNS, connect, TLS or protocol failure.</summary>
    Network,

    /// <summary>The remote server answered with a 4xx or 5xx; <see cref="HttpFetchResponse.Status"/> names it.</summary>
    HttpError,

    /// <summary>This render has already made as many requests as it may.</summary>
    TooManyRequests,

    /// <summary>The redirect limit was reached; <see cref="HttpFetchResponse.Status"/> is the last 3xx.</summary>
    TooManyRedirects,
}
