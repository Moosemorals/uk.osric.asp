// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core implementation of a page's read-only view of the store.</summary>
/// <remarks>
/// <para>
/// Every call creates and disposes its own <see cref="StorageDbContext"/>, for
/// <see cref="SqlitePageStateStore"/>'s reason: a render that was abandoned can outlive the request
/// scope, and a page's task must never touch a disposed context.
/// </para>
/// <para>
/// The seam is read-only by construction: there is no <c>SaveChangesAsync</c> call site, no
/// transaction, and nothing that names a <see cref="PageImage"/> or its builder.
/// </para>
/// </remarks>
internal sealed class SqlitePageReadStore(IDbContextFactory<StorageDbContext> contexts) : IPageReadStore {

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageReadHead>> ListAsync(string prefix, int limit, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        // A key may legally contain '%' or '_', so the escape is not decorative: without it a prefix
        // of /100% would match every key that starts /100 followed by any run of characters.
        string like = prefix == "/" ? "/%" : Escape(prefix) + "/%";

        await using StorageDbContext database = contexts.CreateDbContext();

        // The shape and the limit are fixed in the statement, because the row cap needs the order to
        // be decided before the limit is applied.
        var rows = await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == prefix || EF.Functions.Like(head.Path, like, "\\"))
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (head, revision) => new {
                    head.Path,
                    head.IsDeleted,
                    revision.SavedAt,
                    revision.Hash,
                    revision.TemplateHasCode,
                    revision.RuntimeId,
                    HasImage = revision.Image != null,
                    HasDiagnostics = revision.Diagnostics != null && revision.Diagnostics != "[]",
                })
            .OrderBy(row => row.Path)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => new PageReadHead(
            row.Path, row.SavedAt, row.Hash, row.IsDeleted, row.TemplateHasCode,
            row.RuntimeId, row.HasImage, row.HasDiagnostics))];
    }

    /// <inheritdoc/>
    public async Task<PageReadSource?> GetSourceAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(key);

        await using StorageDbContext database = contexts.CreateDbContext();

        var row = await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == key && !head.IsDeleted)
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (_, revision) => new {
                    revision.Path,
                    revision.Hash,
                    revision.SavedAt,
                    revision.Template,
                })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new PageReadSource(row.Path, row.Hash, row.SavedAt, row.Template);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageReadRevision>> ListRevisionsAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(key);

        await using StorageDbContext database = contexts.CreateDbContext();

        var rows = await database.Set<PageRevision>().AsNoTracking()
            .Where(revision => revision.Path == key)
            .Select(revision => new {
                revision.Hash,
                revision.SavedAt,
                IsCurrent = database.Set<PageHead>().Any(head =>
                    head.Path == key
                    && !head.IsDeleted
                    && head.RevisionId == revision.RevisionId),
            })
            .ToArrayAsync(cancellationToken);

        return [.. rows
            .OrderByDescending(row => row.SavedAt)
            .ThenByDescending(row => row.Hash, StringComparer.Ordinal)
            .Select(row => new PageReadRevision(row.Hash, row.SavedAt, row.IsCurrent))];
    }

    /// <inheritdoc/>
    public async Task<PageReadSource?> GetRevisionSourceAsync(string key, string hash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(hash);

        await using StorageDbContext database = contexts.CreateDbContext();

        var row = await database.Set<PageRevision>().AsNoTracking()
            .Where(revision => revision.Path == key && revision.Hash == hash)
            .Select(revision => new {
                revision.Path,
                revision.Hash,
                revision.SavedAt,
                revision.Template,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new PageReadSource(row.Path, row.Hash, row.SavedAt, row.Template);
    }

    // The LIKE pattern's own metacharacters, escaped so a prefix is a literal subtree. The escape
    // character itself comes first, or escaping it would double the escapes it just wrote.
    private static string Escape(string prefix) =>
        prefix
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
