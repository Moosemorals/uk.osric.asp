// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What an outbound fetch produced.</summary>
/// <remarks>
/// Every transport outcome is a value, so a page that shows live data can branch on
/// <see cref="Failure"/> instead of catching. On a transport failure <see cref="Status"/> is
/// <c>0</c>, the headers and body are empty and <see cref="Truncated"/> is false. An HTTP error
/// response keeps its real status, so a page can test <c>Status &gt;= 500</c> or a specific code such
/// as <c>404</c>.
/// </remarks>
/// <param name="Status">The HTTP status, or <c>0</c> when no response was received.</param>
/// <param name="Headers">The response headers the host returns: a small allow-list plus any <c>X-</c> name.</param>
/// <param name="Body">The response body decoded as text, possibly truncated.</param>
/// <param name="Failure">Why the fetch failed, or <see cref="PageFetchFailure.None"/>.</param>
/// <param name="Truncated">Whether the body hit the host's response cap.</param>
public sealed record PageFetchResponse(
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    PageFetchFailure Failure,
    bool Truncated) {

    /// <summary>Whether this was a 2xx response with no transport or HTTP failure.</summary>
    public bool Succeeded => Failure == PageFetchFailure.None && Status is >= 200 and < 300;
}
