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
/// The deadline arrives after construction, from the renderer, so the capability reads it per call
/// rather than capturing a token that did not exist yet.
/// </para>
/// </remarks>
internal sealed class PageHttpFetch(
    PagePath path,
    bool isHead,
    CancellationToken aborted,
    IPageFetcher fetcher,
    FetchBudget budget,
    PageMetrics metrics,
    ILogger logger) : RenderBoundCapability, IHttpFetch, IRenderLifetimeAware {
#pragma warning restore CA1068 // CancellationToken parameters must come last

    private readonly Lock _streamLock = new();
    private readonly List<HttpFetchStream> _streams = [];
    private bool _lifetimeEnded;

    /// <inheritdoc/>
    public async Task<HttpFetchResponse> SendAsync(HttpFetchRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot fetch.");
        }

        // A HEAD render may read but never POST; PageEndpoint says why a HEAD runs at all.
        if (request.Method == HttpFetchMethod.Post && isHead) {
            return Refuse(PageMetrics.FetchInvalid, "a POST during a HEAD request");
        }

        // A request the policy would refuse costs nothing: it is judged before the allowance is
        // asked, so a page experimenting with a header keeps the requests it meant to make.
        if (!fetcher.TryValidate(request, path.Key, out string why)) {
            return Refuse(PageMetrics.FetchInvalid, why);
        }

        if (!budget.TryTake()) {
            return Refuse(PageMetrics.FetchLimit, budget.DescribeRefusal());
        }

        return await fetcher.FetchAsync(request, path.Key, aborted, Deadline);
    }

    /// <inheritdoc/>
    public async Task<HttpFetchStream> StreamAsync(HttpFetchRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot fetch.");
        }

        if (request.Method == HttpFetchMethod.Post && isHead) {
            return RefuseStream(PageMetrics.FetchInvalid, "a POST during a HEAD request");
        }

        if (!fetcher.TryValidate(request, path.Key, out string why)) {
            return RefuseStream(PageMetrics.FetchInvalid, why);
        }

        if (!budget.TryTake()) {
            return RefuseStream(PageMetrics.FetchLimit, budget.DescribeRefusal());
        }

        HttpFetchStream stream = await fetcher.StreamAsync(request, path.Key, aborted, Deadline);
        bool closeImmediately;
        lock (_streamLock) {
            closeImmediately = _lifetimeEnded || IsAbandoned;
            if (!closeImmediately) {
                _streams.Add(stream);
            }
        }

        if (closeImmediately) {
            await stream.CloseAsync();
        }

        return stream;
    }

    /// <inheritdoc/>
    public void AttachLifetime(RenderLifetime lifetime) {
        ArgumentNullException.ThrowIfNull(lifetime);

        lifetime.RegisterCleanup(CloseStreamsAsync);
    }

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

    private HttpFetchStream RefuseStream(string outcome, string why) {
        return new EmptyHttpFetchStream(Refuse(outcome, why).Failure);
    }

    private async Task CloseStreamsAsync() {
        HttpFetchStream[] streams;
        lock (_streamLock) {
            _lifetimeEnded = true;
            streams = [.. _streams];
            _streams.Clear();
        }

        await Task.WhenAll(streams.Select(stream => stream.CloseAsync()));
    }
}
