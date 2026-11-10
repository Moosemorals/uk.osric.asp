// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Assistant;

/// <summary>An <see cref="IAssistantFetcher"/> that answers from a script rather than a socket.</summary>
internal sealed class FakeAssistantFetcher(Func<string, HttpFetchResponse>? answer = null) : IAssistantFetcher {

    private readonly Func<string, HttpFetchResponse> _answer = answer ?? (_ => new HttpFetchResponse(
        200,
        new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8" },
        "ok",
        HttpFetchFailure.None,
        false));

    public List<string> Requests { get; } = [];

    public Task<HttpFetchResponse> FetchAsync(string url, CancellationToken cancellationToken) {
        Requests.Add(url);

        return Task.FromResult(_answer(url));
    }
}
