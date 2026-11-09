// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The <see cref="IPageRevisionStore"/> over a context factory.</summary>
/// <remarks>
/// One context per save, created and disposed here. It calls the same
/// <see cref="SqlitePageStore.StoreRevisionAsync"/> the editor's save and an imported batch call, so
/// a page's save is one more caller of the one insert rather than a second writer (DEC-192).
/// </remarks>
internal sealed class SqlitePageRevisionStore(
    IDbContextFactory<StorageDbContext> factory,
    ILogger<SqlitePageRevisionStore> logger) : IPageRevisionStore {

    /// <inheritdoc/>
    public async Task<PageTemplate> SaveAsync(PageTemplate revision, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(revision);

        await using StorageDbContext database = factory.CreateDbContext();

        return await SqlitePageStore.StoreRevisionAsync(database, revision, logger, cancellationToken);
    }
}
