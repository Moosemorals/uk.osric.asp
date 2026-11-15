// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Work;

/// <summary>The page store as the harness's jobs read it: the one store the test already writes to.</summary>
internal sealed class FakeWorkerStore(FakePageStore storage) : IWorkerStore {

    /// <summary>How often a job read the worker.</summary>
    public int ReadCount { get; private set; }

    /// <inheritdoc/>
    public async Task<PageTemplate?> GetAsync(PagePath worker, CancellationToken cancellationToken = default) {
        ReadCount++;

        return await storage.GetPageAsync(worker, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> TrySetImageAsync(
        string revisionId,
        string template,
        PageImage image,
        CancellationToken cancellationToken = default) {
        return storage.TrySetImageAsync(revisionId, template, image, cancellationToken);
    }
}
