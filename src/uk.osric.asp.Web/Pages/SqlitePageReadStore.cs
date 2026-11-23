// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core implementation of a page's read-only view of the store.</summary>
/// <remarks>
/// <para>
/// Every call creates and disposes its own <see cref="StorageDbContext"/>, for
/// <see cref="SqliteStoreValues"/>'s reason: a render that was abandoned can outlive the request
/// scope, and a page's task must never touch a disposed context.
/// </para>
/// <para>
/// The seam is read-only by construction: there is no <c>SaveChangesAsync</c> call site, and nothing
/// that names a <see cref="PageImage"/> or its builder. A listing opens one deferred read
/// transaction so its rows and the prefix's fingerprint come from the same snapshot: in WAL the
/// snapshot is taken at the first statement, and a deferred <c>BEGIN</c> takes no write lock, so it
/// waits for no writer and pins nothing beyond the two statements.
/// </para>
/// </remarks>
internal sealed class SqlitePageReadStore(IDbContextFactory<StorageDbContext> contexts) : IPageReadStore {

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageReadHead>> ListLibrariesAsync(CancellationToken cancellationToken = default) {
        await using StorageDbContext database = contexts.CreateDbContext();

        var rows = await database.Set<PageHead>().AsNoTracking()
            .Where(head => !head.IsDeleted)
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (head, revision) => new {
                    head.Path,
                    head.SeedHash,
                    head.ContentHash,
                    revision.SavedAt,
                    revision.Hash,
                    revision.TemplateKind,
                    revision.TemplateLibraries,
                    revision.RuntimeId,
                    HasImage = revision.Image != null,
                    HasDiagnostics = revision.Diagnostics != null && revision.Diagnostics != "[]",
                })
            .Where(row => row.TemplateKind == PageKind.Library)
            .OrderBy(row => row.Path)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => new PageReadHead(
            row.Path, row.SavedAt, row.Hash, IsDeleted: false, row.TemplateKind,
            row.RuntimeId, row.HasImage, row.HasDiagnostics, row.SeedHash, row.ContentHash,
            row.TemplateLibraries))];
    }

    /// <inheritdoc/>
    public Task<PageReadSlice> ListAsync(
        string prefix, string? after, int limit, bool recursive = true, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        return InReadSnapshotAsync(async (database, token) => {
            // The shape and the limit are fixed in the statement, because the row limit needs the order
            // to be decided before the limit is applied. The cursor is a key rather than a count, so
            // a save between two calls cannot make the next one skip a row or repeat one.
            IQueryable<PageHead> heads = HeadsUnder(database, prefix, recursive);

            if (after is not null) {
                heads = heads.Where(head => head.Path.CompareTo(after) > 0);
            }

            var rows = await heads
                .Join(database.Set<PageRevision>().AsNoTracking(),
                    head => new { head.RevisionId, head.Path },
                    revision => new { revision.RevisionId, revision.Path },
                    (head, revision) => new {
                        head.Path,
                        head.IsDeleted,
                        head.SeedHash,
                        head.ContentHash,
                        revision.SavedAt,
                        revision.Hash,
                        revision.TemplateKind,
                        revision.TemplateLibraries,
                        revision.RuntimeId,
                        HasImage = revision.Image != null,
                        HasDiagnostics = revision.Diagnostics != null && revision.Diagnostics != "[]",
                    })
                .OrderBy(row => row.Path)
                .Take(limit)
                .ToArrayAsync(token);

            PageFingerprintFact[] facts = await FactsUnderAsync(database, prefix, token);

            return new PageReadSlice(
                [.. rows.Select(row => new PageReadHead(
                    row.Path, row.SavedAt, row.Hash, row.IsDeleted, row.TemplateKind,
                    row.RuntimeId, row.HasImage, row.HasDiagnostics, row.SeedHash, row.ContentHash,
                    row.TemplateLibraries))],
                PageSubtreeFingerprint.Of(facts));
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<PageReadSource> ScanAsync(
        string prefix,
        string? from,
        bool recursive = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(prefix);

        await using StorageDbContext database = contexts.CreateDbContext();

        IQueryable<PageHead> heads = HeadsUnder(database, prefix, recursive)
            .Where(head => !head.IsDeleted);

        if (from is not null) {
            heads = heads.Where(head => head.Path.CompareTo(from) >= 0);
        }

        IAsyncEnumerable<PageReadSource> rows = heads
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (head, revision) => new {
                    revision.Path,
                    revision.Hash,
                    revision.SavedAt,
                    revision.Template,
                })
            .OrderBy(row => row.Path)
            .Select(row => new PageReadSource(row.Path, row.Hash, row.SavedAt, row.Template))
            .AsAsyncEnumerable();

        await foreach (PageReadSource source in rows.WithCancellation(cancellationToken)) {
            yield return source;
        }
    }

    /// <inheritdoc/>
    public async Task<string> VersionAsync(string prefix, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(prefix);

        await using StorageDbContext database = contexts.CreateDbContext();

        PageFingerprintFact[] facts = await FactsUnderAsync(database, prefix, cancellationToken);

        return PageSubtreeFingerprint.Of(facts);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListFoldersAsync(string prefix, string? after, int limit, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        string like = prefix == "/" ? "/%" : Escape(prefix) + "/%";

        // A cursor names a child the slice which answered it has already reported, so the keys below
        // it are not a second folder for the same key.
        string cursorSubtree = after is null ? string.Empty : Escape(after) + "/%";

        await using StorageDbContext database = contexts.CreateDbContext();

        // A child is a folder when a key lives below it, and its segment is its text up to the next
        // slash, which EF Core cannot project — so this one statement is raw. `length()` and `substr`
        // count characters; a length measured in C# counts UTF-16 units, and a key holding astral
        // characters would be cut in the wrong place.
        string[] folders = await database.Database.SqlQuery<string>($"""
            SELECT DISTINCT substr("Path", 1, instr(substr("Path", length({prefix}) + 2), '/') + length({prefix})) AS "Value"
            FROM "PageHeads"
            WHERE "Path" LIKE {like} ESCAPE '\'
              AND instr(substr("Path", length({prefix}) + 2), '/') > 0
              AND ({after} IS NULL OR ("Path" > {after} AND "Path" NOT LIKE {cursorSubtree} ESCAPE '\'))
            ORDER BY "Value"
            LIMIT {limit}
            """).ToArrayAsync(cancellationToken);

        return folders;
    }

    /// <inheritdoc/>
    public async Task<PageReadHead?> GetHeadAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(key);

        await using StorageDbContext database = contexts.CreateDbContext();

        var row = await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == key)
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (head, revision) => new {
                    head.Path,
                    head.IsDeleted,
                    head.SeedHash,
                    head.ContentHash,
                    revision.SavedAt,
                    revision.Hash,
                    revision.TemplateKind,
                    revision.TemplateLibraries,
                    revision.RuntimeId,
                    HasImage = revision.Image != null,
                    HasDiagnostics = revision.Diagnostics != null && revision.Diagnostics != "[]",
                })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new PageReadHead(
                row.Path, row.SavedAt, row.Hash, row.IsDeleted, row.TemplateKind,
                row.RuntimeId, row.HasImage, row.HasDiagnostics, row.SeedHash, row.ContentHash,
                row.TemplateLibraries);
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

    // One deferred BEGIN holds the snapshot for the statements inside it. In WAL the snapshot is
    // taken at the first statement, and a read transaction takes no write lock, so this is a reader
    // rather than the store's one writer.
    private async Task<T> InReadSnapshotAsync<T>(
        Func<StorageDbContext, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken) {
        await using StorageDbContext database = contexts.CreateDbContext();
        await database.Database.OpenConnectionAsync(cancellationToken);

        try {
            SqliteConnection connection = (SqliteConnection)database.Database.GetDbConnection();
#pragma warning disable CA1849 // Microsoft.Data.Sqlite has no async overload that keeps the transaction deferred.
            await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
#pragma warning restore CA1849
            _ = await database.Database.UseTransactionAsync(transaction, cancellationToken);

            T answer = await read(database, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return answer;
        } finally {
            await database.Database.CloseConnectionAsync();
        }
    }

    // The fact struct names exactly the columns a listing row's identity and seed sentence use, so the
    // scan projects five columns rather than materialising a PageHead entity.
    private static async Task<PageFingerprintFact[]> FactsUnderAsync(
        StorageDbContext database,
        string prefix,
        CancellationToken cancellationToken) {
        var facts = await HeadsUnder(database, prefix)
            .Select(head => new {
                head.Path,
                head.RevisionId,
                head.IsDeleted,
                head.SeedHash,
                head.ContentHash,
            })
            .ToArrayAsync(cancellationToken);

        return [.. facts.Select(fact => new PageFingerprintFact(
            fact.Path, fact.RevisionId, fact.IsDeleted, fact.SeedHash, fact.ContentHash))];
    }

    // A key may legally contain '%' or '_', so the escape is not decorative: without it a prefix of
    // /100% would match every key that starts /100 followed by any run of characters. A direct child
    // is one segment below the prefix, so a key with another slash after that is a grandchild; the
    // root's own subtree pattern already special-cases it. The fingerprint always asks for the whole
    // prefix, because a one-level answer is still a slice of that prefix's list.
    private static IQueryable<PageHead> HeadsUnder(StorageDbContext database, string prefix, bool recursive = true) {
        string like = prefix == "/" ? "/%" : Escape(prefix) + "/%";
        string deeper = prefix == "/" ? "/%/%" : Escape(prefix) + "/%/%";

        IQueryable<PageHead> heads = database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == prefix || EF.Functions.Like(head.Path, like, "\\"));

        return recursive ? heads : heads.Where(head => !EF.Functions.Like(head.Path, deeper, "\\"));
    }

    // The LIKE pattern's own metacharacters, escaped so a prefix is a literal subtree. The escape
    // character itself comes first, or escaping it would double the escapes it just wrote.
    private static string Escape(string prefix) =>
        prefix
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
