// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Work;

using Microsoft.Extensions.DependencyInjection;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>The page store as a job reads it: one worker, and the image it was compiled to.</summary>
/// <remarks>
/// It exists because a job outlives the request that started it while <see cref="IPageStore"/> is
/// scoped to one, so the singleton that runs jobs cannot hold it. Every call owns its scope, the way
/// the page store's read seam does.
/// </remarks>
internal interface IWorkerStore {

    /// <summary>Reads the worker stored at a key.</summary>
    /// <param name="worker">The worker's canonical key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The revision the head points at, or <see langword="null"/> when nothing is stored there.</returns>
    Task<PageTemplate?> GetAsync(PagePath worker, CancellationToken cancellationToken = default);

    /// <summary>Stores a rebuilt worker's image against the revision it was built from.</summary>
    /// <param name="revisionId">The revision the image belongs to.</param>
    /// <param name="template">The template it was built from.</param>
    /// <param name="image">The image to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the revision still had that template, so the write was kept.</returns>
    Task<bool> TrySetImageAsync(
        string revisionId,
        string template,
        PageImage image,
        CancellationToken cancellationToken = default);
}

/// <summary>One scope per call, because a job's own scope may be doing something else.</summary>
internal sealed class ScopedWorkerStore(IServiceScopeFactory scopes) : IWorkerStore {

    /// <inheritdoc/>
    public async Task<PageTemplate?> GetAsync(PagePath worker, CancellationToken cancellationToken = default) {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IPageStore store = scope.ServiceProvider.GetRequiredService<IPageStore>();

        return await store.GetPageAsync(worker, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> TrySetImageAsync(
        string revisionId,
        string template,
        PageImage image,
        CancellationToken cancellationToken = default) {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IPageStore store = scope.ServiceProvider.GetRequiredService<IPageStore>();

        return await store.TrySetImageAsync(revisionId, template, image, cancellationToken);
    }
}
