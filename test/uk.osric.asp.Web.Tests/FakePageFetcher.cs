// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>A <see cref="IPageFetcher"/> that answers from a script rather than a socket.</summary>
internal sealed class FakePageFetcher(Func<HttpFetchRequest, HttpFetchResponse>? answer = null) : IPageFetcher {

    private readonly Func<HttpFetchRequest, HttpFetchResponse> _answer = answer ?? (request => new HttpFetchResponse(
        0,
        new Dictionary<string, string>(),
        string.Empty,
        HttpFetchFailure.Network,
        false));

    public List<HttpFetchRequest> Requests { get; } = [];

    public List<HttpFetchRequest> StreamRequests { get; } = [];

    public Func<HttpFetchRequest, HttpFetchStream>? StreamAnswer { get; set; }

    public string? LastPagePath { get; private set; }

    public CancellationToken LastAborted { get; private set; }

    public CancellationToken LastDeadline { get; private set; }

    public Task<HttpFetchResponse> FetchAsync(
        HttpFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline) {
        Requests.Add(request);
        LastPagePath = pagePath;
        LastAborted = aborted;
        LastDeadline = deadline;

        return Task.FromResult(_answer(request));
    }

    public Task<HttpFetchStream> StreamAsync(
        HttpFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline) {
        StreamRequests.Add(request);
        LastPagePath = pagePath;
        LastAborted = aborted;
        LastDeadline = deadline;

        return Task.FromResult(StreamAnswer?.Invoke(request) ?? new FakeHttpFetchStream());
    }
}

internal sealed class FakeHttpFetchStream(params string?[] lines) : HttpFetchStream {
    private readonly Queue<string?> _lines = new(lines);

    public bool IsClosed { get; private set; }

    public override int Status => 200;

    public override IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

    public override HttpFetchFailure Failure => HttpFetchFailure.None;

    public override bool Truncated => false;

    public override Task<string?> ReadLineAsync() {
        if (IsClosed || !_lines.TryDequeue(out string? line)) {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(line);
    }

    public override Task CloseAsync() {
        IsClosed = true;

        return Task.CompletedTask;
    }
}
