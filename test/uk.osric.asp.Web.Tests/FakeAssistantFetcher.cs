// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

/// <summary>An <see cref="IAssistantFetcher"/> that answers from a script rather than a socket.</summary>
internal sealed class FakeAssistantFetcher(Func<string, HttpFetchResponse>? answer = null) : IAssistantFetcher {

    private readonly Func<string, HttpFetchResponse> _answer = answer ?? (_ => new HttpFetchResponse(
        200,
        new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8" },
        "ok",
        HttpFetchFailure.None,
        false));

    public List<string> Requests { get; } = [];

    /// <summary>What a view answered with, when a test scripts the transport's own shape.</summary>
    public Func<string, OutboundResponse>? ViewResponse { get; init; }

    /// <summary>Every path a view asked for, in order.</summary>
    public List<string> Viewed { get; } = [];

    /// <summary>The token each view presented, in the order they were asked for.</summary>
    public List<ViewToken?> ViewTokens { get; } = [];

    public Task<HttpFetchResponse> FetchAsync(string url, CancellationToken cancellationToken) {
        Requests.Add(url);

        return Task.FromResult(_answer(url));
    }

    public Task<OutboundResponse> ViewAsync(string url, ViewToken token, CancellationToken cancellationToken) {
        Viewed.Add(url);
        ViewTokens.Add(token);

        if (ViewResponse is { } scripted) {
            return Task.FromResult(scripted(url));
        }

        HttpFetchResponse answer = _answer(url);

        return Task.FromResult(new OutboundResponse(
            answer.Status,
            answer.Headers,
            Encoding.UTF8.GetBytes(answer.Body),
            answer.Failure,
            answer.Truncated,
            []));
    }
}
