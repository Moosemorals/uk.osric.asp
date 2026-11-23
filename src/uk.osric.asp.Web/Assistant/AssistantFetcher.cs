// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

/// <summary>Gives the assistant the same bounded outbound HTTP as a page, with no allow-list.</summary>
/// <remarks>
/// <para>
/// A GET only: neither tool has a method or header argument. The URL rules, redirects, decoding and
/// response limit are <see cref="OutboundFetcher"/>'s, so the assistant cannot reach a connection a page
/// cannot (`pages-outbound-fetch`).
/// </para>
/// <para>
/// <c>view</c> is the same transport with one credential: the run's token, which
/// <see cref="ViewCredential"/> offers to <c>Site:Origin</c> and to nothing else, hop by hop. A
/// redirect off that origin drops the header, so a page cannot take the run's identity anywhere.
/// </para>
/// </remarks>
internal sealed class AssistantFetcher(SiteOptions site, OutboundFetcher fetcher) : IAssistantFetcher {

    /// <inheritdoc/>
    public Task<HttpFetchResponse> FetchAsync(string url, CancellationToken cancellationToken) {
        // An empty URL is the model's mistake rather than a transport failure; it must not become an
        // ArgumentException the run reports as a broken tool.
        if (string.IsNullOrWhiteSpace(url)) {
            return Task.FromResult(new HttpFetchResponse(
                0,
                new Dictionary<string, string>(),
                string.Empty,
                HttpFetchFailure.InvalidRequest,
                false));
        }

        return fetcher.FetchAsync(
            new HttpFetchRequest(url),
            "the assistant",
            OutboundOrigin.Assistant,
            aborted: cancellationToken,
            deadline: cancellationToken);
    }

    /// <inheritdoc/>
    public Task<OutboundResponse> ViewAsync(string url, ViewToken token, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(token);

        return fetcher.FetchResponseAsync(
            new HttpFetchRequest(url),
            "the assistant",
            OutboundOrigin.Assistant,
            new ViewCredential(site.Origin, token.Secret),
            aborted: cancellationToken,
            deadline: cancellationToken);
    }
}
