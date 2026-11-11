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
/// page may fetch any <c>http</c> or <c>https</c> host (DEC-216). A named credential is resolved and
/// bound to this page here, before <see cref="OutboundFetcher"/> opens a socket.
/// </remarks>
internal sealed class PageFetcher(
    OutboundFetcher fetcher,
    PageFetchOptions options,
    PageMetrics metrics,
    ILogger<PageFetcher> logger) : IPageFetcher {

    /// <inheritdoc/>
    public async Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);

        PagePath path = PagePath.Normalize(pagePath);
        long start = Stopwatch.GetTimestamp();
        HttpFetchResponse response = await SendAsync(request, path, aborted, deadline);

        metrics.RecordFetch(path, OutcomeOf(response), Stopwatch.GetElapsedTime(start));

        return response;
    }

    private async Task<HttpFetchResponse> SendAsync(
        HttpFetchRequest request,
        PagePath path,
        CancellationToken aborted,
        CancellationToken deadline) {
        PageCredential? credential = null;

        if (request.Credential is not null) {
            if (!options.Credentials.TryGetValue(request.Credential, out credential)) {
                return Refuse(path, request.Url, "an unknown credential was named");
            }

            if (!credential.IsBoundTo(path.Key)) {
                return Refuse(path, request.Url, $"{credential.Name} is bound to another page");
            }

            if (request.Headers.Keys.Any(header => string.Equals(header, credential.Header, StringComparison.OrdinalIgnoreCase))) {
                return Refuse(path, request.Url, $"the request sets {credential.Header}, which {credential.Name} also sets");
            }
        }

        return await fetcher.FetchAsync(request, path.Key, OutboundOrigin.Page, credential, aborted, deadline);
    }

    private HttpFetchResponse Refuse(PagePath path, string url, string reason) {
        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.IdnHost : "(unparsed)";
        logger.LogWarning("Page {Path} was refused a fetch to {Host}: {Reason}", path.Key, host, reason);

        return new HttpFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            HttpFetchFailure.InvalidRequest,
            false);
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
