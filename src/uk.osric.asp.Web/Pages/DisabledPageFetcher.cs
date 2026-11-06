// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The fetcher a page gets before the real one is wired into the context.</summary>
/// <remarks>
/// Every request is refused as <see cref="PageFetchFailure.NotAllowed"/> without a socket being
/// opened, which is also what a real fetcher does when no host is allow-listed.
/// </remarks>
internal sealed class DisabledPageFetcher : IPageFetcher {

    /// <summary>The one instance, because it holds nothing.</summary>
    public static DisabledPageFetcher Instance { get; } = new();

    /// <inheritdoc/>
    public Task<PageFetchResponse> FetchAsync(
        PageFetchRequest request,
        string pagePath,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(pagePath);

        return Task.FromResult(new PageFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            PageFetchFailure.NotAllowed,
            false));
    }
}
