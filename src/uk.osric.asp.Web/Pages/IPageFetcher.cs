// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The seam <see cref="SimpleContext"/> calls for an outbound fetch.</summary>
/// <remarks>
/// A test replaces this to prove the page contract without a socket, and the integration suite
/// replaces it to prove the wiring; the real implementation is the only thing that opens one.
/// </remarks>
internal interface IPageFetcher {
    /// <summary>Makes one bounded outbound request.</summary>
    /// <param name="request">The page's request.</param>
    /// <param name="pagePath">The page's key, for the metric and log tags.</param>
    /// <param name="cancellationToken">Already combines the request's abort token and the render deadline.</param>
    /// <returns>The response; a failure is reported in the value, not thrown.</returns>
    Task<PageFetchResponse> FetchAsync(
        PageFetchRequest request,
        string pagePath,
        CancellationToken cancellationToken);
}
