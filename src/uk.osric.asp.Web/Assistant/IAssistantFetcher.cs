// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Sdk;

/// <summary>Performs the one outbound GET the assistant's <c>fetch</c> tool offers.</summary>
internal interface IAssistantFetcher {

    /// <summary>GETs <paramref name="url"/> and returns the response, or a failure value.</summary>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL.</param>
    /// <param name="cancellationToken">Cancelled when the tool's budget or the run ends.</param>
    Task<HttpFetchResponse> FetchAsync(string url, CancellationToken cancellationToken);
}
