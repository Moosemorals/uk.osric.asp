// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Web.Storage;

/// <summary>Fills in the content hash of every head written before the column existed.</summary>
/// <remarks>
/// <para>
/// The anchor a seed write records cannot be recovered, and is not invented: a repaired page stays
/// unknown until something establishes where it came from. The content hash can be recovered exactly,
/// because the head's revision holds the source it was computed from. That is enough to tell a legacy
/// page that matches its shipped seed (adoptable) from one that does not (unknown).
/// </para>
/// <para>
/// It runs once after the migration and before the start seeder, in one transaction, and is
/// idempotent: a row that already has a hash is never read. It is deliberately not inside the EF
/// migration — SQLite has no SHA-256 and migrations get no context, so the alternative would be a
/// frozen second copy of the algorithm.
/// </para>
/// </remarks>
internal static class PageContentHashRepair {

    /// <summary>Repairs every head whose content hash is missing.</summary>
    /// <param name="database">The store to repair.</param>
    /// <param name="cancellationToken">Cancels the repair.</param>
    /// <returns>How many heads it repaired.</returns>
    public static async Task<int> RunAsync(StorageDbContext database, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(database);

        var rows = await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.ContentHash == null)
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (head, revision) => new { head.Path, revision.Template })
            .ToArrayAsync(cancellationToken);

        if (rows.Length == 0) {
            return 0;
        }

        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        foreach (var row in rows) {
            string contentHash = TemplateFingerprint.Of(row.Template);
            await database.Database.ExecuteSqlAsync($"""
                UPDATE "PageHeads" SET "ContentHash" = {contentHash}
                WHERE "Path" = {row.Path} AND "ContentHash" IS NULL
                """, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return rows.Length;
    }
}
