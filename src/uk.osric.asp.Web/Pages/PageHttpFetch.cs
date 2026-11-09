// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

#pragma warning disable CA1068 // CancellationToken parameters must come last
/// <summary>The host's <see cref="IHttpFetch"/>, bound to one layer and the render's budget.</summary>
/// <remarks>
/// <para>
/// Every refusal is a value rather than an exception, so a page can fall back without a <c>try</c>.
/// Only abandonment throws: the render has already been answered, so there is no page left to read
/// a value.
/// </para>
/// <para>
/// The deadline arrives after construction, from the renderer, so the feature reads it per call
/// rather than capturing a token that did not exist yet.
/// </para>
/// </remarks>
internal sealed class PageHttpFetch(
    PagePath path,
    bool isHead,
    CancellationToken aborted,
    IPageFetcher fetcher,
    PageFetchOptions options,
    RenderFetchBudget budget,
    PageMetrics metrics,
    ILogger logger) : IHttpFetch, IRenderBoundFeature {
#pragma warning restore CA1068 // CancellationToken parameters must come last

    private CancellationToken _deadline;
    private volatile bool _abandoned;

    /// <inheritdoc/>
    public async Task<HttpFetchResponse> SendAsync(HttpFetchRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        if (_abandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot fetch.");
        }

        // A HEAD render may read but never POST; PageEndpoint says why a HEAD runs at all.
        if (request.Method == HttpFetchMethod.Post && isHead) {
            return Refuse(PageMetrics.FetchInvalid, "a POST during a HEAD request");
        }

        if (!budget.TryTake()) {
            return Refuse(PageMetrics.FetchLimit, $"more than {options.MaxRequestsPerRender} requests");
        }

        return await fetcher.FetchAsync(request, path.Key, aborted, _deadline);
    }

    /// <inheritdoc/>
    public void AttachDeadline(CancellationToken deadline) => _deadline = deadline;

    /// <inheritdoc/>
    public void Abandon() => _abandoned = true;

    private HttpFetchResponse Refuse(string outcome, string why) {
        metrics.RecordFetch(path, outcome, TimeSpan.Zero);
        logger.LogWarning("Page {Path} was refused a fetch for {Reason}", path.Key, why);

        return new HttpFetchResponse(
            0,
            new Dictionary<string, string>(),
            string.Empty,
            outcome == PageMetrics.FetchLimit ? HttpFetchFailure.TooManyRequests : HttpFetchFailure.InvalidRequest,
            false);
    }
}
