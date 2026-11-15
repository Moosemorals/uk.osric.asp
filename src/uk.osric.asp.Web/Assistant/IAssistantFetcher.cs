// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

/// <summary>The outbound GETs the assistant's tools offer: <c>fetch</c> and <c>view</c>.</summary>
internal interface IAssistantFetcher {

    /// <summary>GETs <paramref name="url"/> anonymously and returns the response, or a failure value.</summary>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL.</param>
    /// <param name="cancellationToken">Cancelled when the tool's budget or the run ends.</param>
    Task<HttpFetchResponse> FetchAsync(string url, CancellationToken cancellationToken);

    /// <summary>GETs this site and answers the response itself, authenticated as the run's owner.</summary>
    /// <param name="url">An absolute URL on <c>Site:Origin</c>, resolved by the caller.</param>
    /// <param name="token">The run's view token, which the fetcher attaches to that origin alone.</param>
    /// <param name="cancellationToken">Cancelled when the tool's budget or the run ends.</param>
    /// <remarks>
    /// The answer is the transport's own — every header, the body's bytes, and the redirects it took —
    /// because a view that summarised the response would be a second guess at what the page did.
    /// </remarks>
    Task<OutboundResponse> ViewAsync(string url, ViewToken token, CancellationToken cancellationToken);
}
