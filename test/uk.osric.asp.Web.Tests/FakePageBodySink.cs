// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>Records what a flushing context committed and wrote.</summary>
internal sealed class FakePageBodySink : IPageBodySink {

    public List<PageResponse> Started { get; } = [];

    public List<string> Chunks { get; } = [];

    public Task StartAsync(PageResponse response, CancellationToken cancellationToken) {
        Started.Add(response);

        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) {
        Chunks.Add(Encoding.UTF8.GetString(bytes.Span));

        return Task.CompletedTask;
    }
}
