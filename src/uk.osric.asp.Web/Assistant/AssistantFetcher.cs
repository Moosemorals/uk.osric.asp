// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Gives the assistant the same bounded outbound HTTP as a page, with no allow-list.</summary>
/// <remarks>
/// A GET only: the tool has no method or header arguments. The URL rules, redirects, text decoding and
/// response cap are <see cref="OutboundFetcher"/>'s, so the assistant cannot reach a connection a page
/// cannot (DEC-216).
/// </remarks>
internal sealed class AssistantFetcher(OutboundFetcher fetcher) : IAssistantFetcher {

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
}
