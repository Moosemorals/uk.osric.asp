// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>A <see cref="IPageFetcher"/> that answers from a script rather than a socket.</summary>
internal sealed class FakePageFetcher(Func<PageFetchRequest, PageFetchResponse>? answer = null) : IPageFetcher {

    private readonly Func<PageFetchRequest, PageFetchResponse> _answer = answer ?? (request => new PageFetchResponse(
        0,
        new Dictionary<string, string>(),
        string.Empty,
        PageFetchFailure.NotAllowed,
        false));

    public List<PageFetchRequest> Requests { get; } = [];

    public string? LastPagePath { get; private set; }

    public CancellationToken LastAborted { get; private set; }

    public CancellationToken LastDeadline { get; private set; }

    public Task<PageFetchResponse> FetchAsync(
        PageFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline) {
        Requests.Add(request);
        LastPagePath = pagePath;
        LastAborted = aborted;
        LastDeadline = deadline;

        return Task.FromResult(_answer(request));
    }
}
