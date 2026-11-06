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
    /// <param name="aborted">Cancelled when the visitor went away.</param>
    /// <param name="deadline">Cancelled when the render's budget ran out.</param>
    /// <returns>The response; a failure is reported in the value, not thrown.</returns>
    /// <remarks>
    /// The two tokens travel separately rather than linked, because a refusal has to say which one
    /// fired: the visitor leaving is <see cref="PageFetchFailure.Aborted"/>, and the render running
    /// out of budget is <see cref="PageFetchFailure.Timeout"/>.
    /// </remarks>
    Task<PageFetchResponse> FetchAsync(
        PageFetchRequest request,
        string pagePath,
        CancellationToken aborted,
        CancellationToken deadline);
}
