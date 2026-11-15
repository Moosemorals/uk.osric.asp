// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>A stream-shaped failure value for a request that received no usable response body.</summary>
internal sealed class EmptyHttpFetchStream(
    HttpFetchFailure failure,
    Action<HttpFetchStream>? onClosed = null) : HttpFetchStream {

    private int _closed;

    /// <inheritdoc/>
    public override int Status => 0;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>();

    /// <inheritdoc/>
    public override HttpFetchFailure Failure { get; } = failure;

    /// <inheritdoc/>
    public override bool Truncated => false;

    /// <inheritdoc/>
    public override Task<string?> ReadLineAsync() {
        Complete();

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc/>
    public override Task CloseAsync() {
        Complete();

        return Task.CompletedTask;
    }

    private void Complete() {
        if (Interlocked.Exchange(ref _closed, 1) == 0) {
            onClosed?.Invoke(this);
        }
    }
}
