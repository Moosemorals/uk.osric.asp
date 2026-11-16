// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>One layer's <see cref="IPageComposition"/>: what it may include, and what may wrap it.</summary>
/// <remarks>
/// A thin adapter over the chain: the chain owns the render, the budgets and the ancestor stack, and
/// this holds the facts that make the calls the caller's — its request, its place in the chain and the
/// response an embed writes into.
/// </remarks>
internal sealed class PageCompositionScope(
    PageChain chain,
    PageCompositionState declared,
    PageRequest callerRequest,
    IReadOnlyList<PageRenderIdentity> path,
    int depth,
    PagePath key,
    Func<SimpleResponse> response) : IPageComposition {

    /// <inheritdoc/>
    public async Task<PageFrame> EmbedAsync(string key, PageRequest? request = null) {
        PageCapture capture = await ComposeAsync(key, request);
        SimpleResponse sink = response();

        // Written first, so a body that does not fit the caller's allowance leaves nothing behind,
        // and charged once: the embedded render spent its own bounded view, not this allowance.
        sink.WriteRaw(capture.Body);

        if (capture.AllowsScripts) {
            sink.AllowScripts();
        }

        return new PageFrame(capture.Title, capture.Head, capture.Stylesheets, capture.AllowsScripts);
    }

    /// <inheritdoc/>
    public Task<PageCapture> CaptureAsync(string key, PageRequest? request = null) => ComposeAsync(key, request);

    /// <inheritdoc/>
    public void Wrap(string key, PageRequest? request = null) {
        PagePath target = Address(key);
        ThrowIfCommitted();

        if (chain.IsReserved(target)) {
            throw new InvalidOperationException($"{target.Key} is a shell path; no page is stored there, so it cannot wrap a page.");
        }

        declared.Wrap(target, request ?? callerRequest, isDefault: false);
    }

    /// <inheritdoc/>
    public void Wrap() {
        ThrowIfCommitted();

        if (chain.DefaultLayout is { } target) {
            declared.Wrap(target, callerRequest, isDefault: true);
        }
    }

    private Task<PageCapture> ComposeAsync(string key, PageRequest? request) {
        PagePath target = Address(key);

        // The page the visitor asked for stays the page on the request, so a page can tell it is
        // inside another by comparing ctx.Path with ctx.Request.Path.
        PageRequest asked = request is null ? callerRequest : request with { Path = callerRequest.Path };

        return chain.ComposeAsync(target, asked, path, depth);
    }

    private static PagePath Address(string key) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        PagePath target = PagePath.Normalize(key);
        return target.IsAddressable
            ? target
            : throw new ArgumentException($"'{key}' is not a page address: a key cannot contain a query or a fragment.", nameof(key));
    }

    private void ThrowIfCommitted() {
        if (response().IsCommitted) {
            throw new InvalidOperationException(
                $"{key.Key} has already flushed its response; it cannot call IPageComposition.Wrap after that.");
        }
    }
}
