// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Diagnostics;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>Makes a page's outbound HTTP request, bounded and metered.</summary>
/// <remarks>
/// The request itself belongs to <see cref="OutboundFetcher"/>; this is the page-shaped edge that
/// names the page in the metric and holds the page's key to the same value contract as the SDK. A
/// page may fetch any <c>http</c> or <c>https</c> host (DEC-216).
/// </remarks>
internal sealed class PageFetcher(
    OutboundFetcher fetcher,
    PageMetrics metrics) : IPageFetcher {

    /// <inheritdoc/>
    public async Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);

        long start = Stopwatch.GetTimestamp();
        HttpFetchResponse response = await fetcher.FetchAsync(request, pagePath, OutboundOrigin.Page, aborted, deadline);

        metrics.RecordFetch(PagePath.Normalize(pagePath), OutcomeOf(response), Stopwatch.GetElapsedTime(start));

        return response;
    }

    private static string OutcomeOf(HttpFetchResponse response) {
        return response.Failure switch {
            HttpFetchFailure.None => PageMetrics.FetchOk,
            HttpFetchFailure.HttpError => PageMetrics.FetchHttpError,
            HttpFetchFailure.Timeout => PageMetrics.FetchTimeout,
            HttpFetchFailure.Network => PageMetrics.FetchNetwork,
            HttpFetchFailure.InvalidRequest => PageMetrics.FetchInvalid,
            HttpFetchFailure.TooManyRequests => PageMetrics.FetchLimit,
            HttpFetchFailure.TooManyRedirects => PageMetrics.FetchRedirectLimit,
            _ => PageMetrics.FetchAborted,
        };
    }
}
