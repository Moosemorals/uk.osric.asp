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
/// page may fetch any <c>http</c> or <c>https</c> host (`pages-outbound-fetch`). A named credential is resolved and
/// bound to this page here, before <see cref="OutboundFetcher"/> opens a socket.
/// </remarks>
internal sealed class PageFetcher(
    OutboundFetcher fetcher,
    PageFetchOptions options,
    PageMetrics metrics,
    ILogger<PageFetcher> logger) : IPageFetcher {

    /// <inheritdoc/>
    public bool TryValidate(HttpFetchRequest request, string pagePath, out string reason) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);

        PagePath path = PagePath.Normalize(pagePath);
        return TryResolveCredential(request, path, out FetchCredential? credential, out reason)
            && OutboundFetcher.TryValidate(request, credential, out reason);
    }

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

    /// <inheritdoc/>
    public async Task<HttpFetchStream> StreamAsync(
        HttpFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);

        PagePath path = PagePath.Normalize(pagePath);
        long start = Stopwatch.GetTimestamp();
        if (!TryResolveCredential(request, path, out FetchCredential? credential, out string why)) {
            LogRefusal(path, request.Url, why);

            return new EmptyHttpFetchStream(HttpFetchFailure.InvalidRequest, stream =>
                metrics.RecordFetch(path, OutcomeOf(stream), Stopwatch.GetElapsedTime(start)));
        }

        return await fetcher.StreamAsync(
            request,
            path.Key,
            OutboundOrigin.Page,
            credential,
            stream => metrics.RecordFetch(path, OutcomeOf(stream), Stopwatch.GetElapsedTime(start)),
            aborted,
            deadline);
    }

    private async Task<HttpFetchResponse> SendAsync(
        HttpFetchRequest request,
        PagePath path,
        CancellationToken aborted,
        CancellationToken deadline) {
        if (!TryResolveCredential(request, path, out FetchCredential? credential, out string why)) {
            LogRefusal(path, request.Url, why);

            return Empty(HttpFetchFailure.InvalidRequest);
        }

        return await fetcher.FetchAsync(request, path.Key, OutboundOrigin.Page, credential, aborted, deadline);
    }

    private bool TryResolveCredential(
        HttpFetchRequest request,
        PagePath path,
        out FetchCredential? credential,
        out string reason) {
        credential = null;
        reason = string.Empty;
        if (request.Credential is null) {
            return true;
        }

        if (!options.Credentials.TryGetValue(request.Credential, out credential)) {
            reason = "an unknown credential was named";

            return false;
        }

        if (!credential.IsBoundTo(path.Key)) {
            reason = $"{credential.Name} is bound to {credential.Scope}";
            credential = null;

            return false;
        }

        foreach (string header in request.Headers.Keys) {
            if (!string.Equals(header, credential.Header, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            reason = $"the request sets {credential.Header}, which {credential.Name} also sets";
            credential = null;

            return false;
        }

        return true;
    }

    private void LogRefusal(PagePath path, string url, string reason) {
        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.IdnHost : "(unparsed)";
        logger.LogWarning("Page {Path} was refused a fetch to {Host}: {Reason}", path.Key, host, reason);
    }

    private static HttpFetchResponse Empty(HttpFetchFailure failure) {
        return new HttpFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            failure,
            false);
    }

    private static string OutcomeOf(HttpFetchResponse response) {
        return OutcomeOf(response.Failure);
    }

    private static string OutcomeOf(HttpFetchStream stream) {
        return OutcomeOf(stream.Failure);
    }

    private static string OutcomeOf(HttpFetchFailure failure) {
        return failure switch {
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
