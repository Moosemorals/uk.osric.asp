// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>Writes an imported batch with one context and one transaction.</summary>
/// <remarks>
/// <para>
/// The commit is short — inserts only, no compilation — so the SQLite write lock is held for
/// milliseconds rather than minutes. Every merged state total is checked before any row is written;
/// if one page would exceed a cap the whole batch is refused and nothing changes.
/// </para>
/// <para>
/// Revisions go first because the head's foreign key points at them, then heads, then state. A crash
/// during the commit is rolled back by SQLite, and a failed write rolls back explicitly, so a reader
/// sees the old pages until the commit and the whole new set after it.
/// </para>
/// </remarks>
internal class SqlitePageBatchStore(
    StorageDbContext database,
    PageStorageOptions options,
    TimeProvider clock,
    ILogger<SqlitePageBatchStore> logger,
    PageStorageRefresh? refresh = null) : IPageBatchStore {

    /// <inheritdoc/>
    public async Task<PageBatchResult> SaveBatchAsync(IReadOnlyList<PageBatchSave> pages, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(pages);

        IReadOnlyList<PageBatchSave> batch = pages;

        for (int attempt = 0; ; attempt++) {
            try {
                await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

                IReadOnlyList<PagePath> refused = await OverflowAsync(batch, cancellationToken);
                if (refused.Count > 0) {
                    await transaction.RollbackAsync(cancellationToken);
                    return new PageBatchResult(refused);
                }

                foreach (PageBatchSave page in batch) {
                    await SqlitePageStore.WriteRevisionAsync(database, page.Template, cancellationToken);
                }

                DateTimeOffset now = clock.GetUtcNow();
                foreach (PageBatchSave page in batch) {
                    foreach (PageStateValue value in page.State) {
                        int written = await WriteStateAsync(database, page.Template.Path, value, now, cancellationToken);
                        if (written == 0) {
                            // A row changed under the batch: the merged total no longer fits.
                            await transaction.RollbackAsync(cancellationToken);
                            return new PageBatchResult([page.Template.Path]);
                        }
                    }
                }

                await transaction.CommitAsync(cancellationToken);
                refresh?.Request();
                return PageBatchResult.Committed;
            } catch (DbUpdateException exception) when (attempt + 1 < SqliteErrors.RetryCount && SqliteErrors.IsUniqueViolation(exception)) {
                // The one failure an ordinary save already retries: a revision id or hash that a
                // concurrent write has just taken. Fresh ids for the whole batch, then try again.
                logger.LogDebug("A concurrent write took a revision id in this batch; regenerating its ids and trying again");
                database.ChangeTracker.Clear();
                batch = [.. batch.Select(page => page with { Template = SqlitePageStore.RegenerateRevision(page.Template) })];
            }
        }
    }

    /// <summary>Writes one state row, under every per-page cap.</summary>
    /// <param name="database">The context the batch transaction runs in.</param>
    /// <param name="path">The page whose state is written.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="updatedAt">The row's timestamp.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>1 when the row was written, 0 when a cap refused it.</returns>
    /// <remarks>
    /// Virtual so a fixture can fail one page's write and prove the whole batch rolls back; the
    /// production path is the same statement a page's own write uses.
    /// </remarks>
    internal virtual Task<int> WriteStateAsync(
        StorageDbContext database,
        PagePath path,
        PageStateValue value,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken) =>
        SqlitePageStateStore.UpsertAsync(database, path, value.Key, value.Value, updatedAt, options, cancellationToken);

    // The keys the archive does not mention keep their rows, so the merged total is what both the
    // existing state and the incoming state would add up to.
    private async Task<IReadOnlyList<PagePath>> OverflowAsync(IReadOnlyList<PageBatchSave> pages, CancellationToken cancellationToken) {
        List<PagePath> refused = [];

        foreach (PageBatchSave page in pages) {
            if (page.State.Count == 0) {
                continue;
            }

            string pagePath = page.Template.Path.Key;
            string[] incoming = [.. page.State.Select(value => value.Key)];

            int otherKeys = await database.Set<PageStateEntry>()
                .CountAsync(row => row.PagePath == pagePath && !incoming.Contains(row.Key), cancellationToken);
            long otherBytes = await database.Set<PageStateEntry>()
                .Where(row => row.PagePath == pagePath && !incoming.Contains(row.Key))
                .SumAsync(row => (long)row.Key.Length + row.Value.Length, cancellationToken);
            long addedBytes = page.State.Sum(value =>
                (long)Encoding.UTF8.GetByteCount(value.Key) + Encoding.UTF8.GetByteCount(value.Value));

            if (otherKeys + page.State.Count > options.MaxKeysPerPage || otherBytes + addedBytes > options.MaxPageBytes) {
                refused.Add(page.Template.Path);
            }
        }

        return refused;
    }
}
