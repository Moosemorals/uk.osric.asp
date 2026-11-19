// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Storage;

/// <summary>The SQLite-backed page store.</summary>
internal sealed class SqlitePageStore(
    StorageDbContext database,
    ILogger<SqlitePageStore> logger,
    PageStorageRefresh? storageRefresh = null,
    string? currentRuntimeId = null) : IPageStore {

    private const int CheckpointAttempts = 3;

    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        Encoder = JsonSerialization.Encoder,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<PageTemplate?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        PageRevision? revision = await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == path.Key && !head.IsDeleted)
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                row => new { row.RevisionId, row.Path },
                (_, row) => row)
            .SingleOrDefaultAsync(cancellationToken);

        return revision is null ? null : ToPage(revision);
    }

    public async Task<IReadOnlyList<PageListRow>> ListPagesAsync(CancellationToken cancellationToken = default) {
        // The head's current revision is joined the way ListRevisionsAsync joins it, and the state is
        // decided client-side because PageImageState cannot be translated into SQL.
        var rows = await database.Set<PageHead>().AsNoTracking()
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (head, revision) => new {
                    head.Path,
                    head.IsDeleted,
                    revision.RuntimeId,
                    revision.TemplateKind,
                    HasImage = revision.Image != null,
                    HasDiagnostics = revision.Diagnostics != null && revision.Diagnostics != "[]",
                })
            .ToArrayAsync(cancellationToken);

        return [.. rows.OrderBy(row => row.Path, StringComparer.Ordinal)
            .Select(row => new PageListRow(
                PagePath.Normalize(row.Path),
                row.IsDeleted,
                PageImageStatus.Describe(row.RuntimeId, row.HasImage, row.HasDiagnostics, currentRuntimeId, row.TemplateKind)))];
    }

    public async Task<IReadOnlyList<PagePath>> ListStoredPathsAsync(CancellationToken cancellationToken = default) {
        string[] heads = await database.Set<PageHead>().AsNoTracking()
            .Select(head => head.Path)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        string[] revisions = await database.Set<PageRevision>().AsNoTracking()
            .Select(revision => revision.Path)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        string[] state = await database.Set<PageStateEntry>().AsNoTracking()
            .Select(row => row.PagePath)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return [.. heads
            .Union(revisions, StringComparer.Ordinal)
            .Union(state, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(PagePath.Normalize)];
    }

    public async Task<string?> GetSourceAsync(PagePath path, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);

        return await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == path.Key && !head.IsDeleted)
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                row => new { row.RevisionId, row.Path },
                (_, row) => row.Template)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default) {
        PageHead? head = await database.Set<PageHead>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Path == path.Key, cancellationToken);

        return head is null
            ? null
            : new PageHeadSummary(PagePath.Normalize(head.Path), head.IsDeleted, head.SeedHash, head.ContentHash);
    }

    public async Task<PageTemplate?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(hash);

        PageRevision? revision = await database.Set<PageRevision>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Hash == hash, cancellationToken);

        return revision is null ? null : ToPage(revision);
    }

    public async Task<IReadOnlyList<PageRevisionSummary>> ListRevisionsAsync(PagePath path, CancellationToken cancellationToken = default) {
        var rows = await database.Set<PageRevision>().AsNoTracking()
            .Where(revision => revision.Path == path.Key)
            .Select(revision => new {
                revision.Hash,
                revision.SavedAt,
                revision.CreationRuntimeId,
                revision.RuntimeId,
                revision.TemplateKind,
                HasImage = revision.Image != null,
                HasDiagnostics = revision.Diagnostics != null && revision.Diagnostics != "[]",
                IsCurrent = database.Set<PageHead>().Any(head =>
                    head.Path == path.Key
                    && !head.IsDeleted
                    && head.RevisionId == revision.RevisionId),
            })
            .ToArrayAsync(cancellationToken);

        return [.. rows.OrderByDescending(row => row.SavedAt)
            .ThenByDescending(row => row.Hash, StringComparer.Ordinal)
            .Select(row => new PageRevisionSummary(
                row.Hash,
                row.SavedAt,
                row.CreationRuntimeId,
                PageImageStatus.Describe(row.RuntimeId, row.HasImage, row.HasDiagnostics, kind: row.TemplateKind),
                row.IsCurrent))];
    }

    public async Task<PageRevisionTemplate?> GetRevisionTemplateByHashAsync(string hash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(hash);

        var revision = await database.Set<PageRevision>().AsNoTracking()
            .Where(row => row.Hash == hash)
            .Select(row => new {
                row.Path,
                row.Hash,
                row.SavedAt,
                row.CreationRuntimeId,
                row.RuntimeId,
                row.TemplateKind,
                HasImage = row.Image != null,
                HasDiagnostics = row.Diagnostics != null && row.Diagnostics != "[]",
                row.Template,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return revision is null
            ? null
            : new PageRevisionTemplate(
                PagePath.Normalize(revision.Path),
                revision.Hash,
                revision.SavedAt,
                revision.CreationRuntimeId,
                PageImageStatus.Describe(revision.RuntimeId, revision.HasImage, revision.HasDiagnostics,
                    kind: revision.TemplateKind),
                revision.Template);
    }

    public Task SavePageAsync(PageTemplate template, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(template);

        return StoreRevisionAsync(database, template, logger, cancellationToken);
    }

    /// <summary>Stores one revision and its head, retrying a revision id another writer took.</summary>
    /// <param name="database">The context to write through.</param>
    /// <param name="template">The revision to store.</param>
    /// <param name="logger">Where a retry is recorded.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The revision that was stored, which is a retry when another writer took the id.</returns>
    /// <remarks>
    /// The editor's save, an imported batch and a page saving its own source all end here, so the
    /// transaction, the row shape and the retry cannot drift apart (`pages-a-page-may-save-itself`). The single save passes
    /// the request's context; a page's save opens one of its own, because its task can outlive the
    /// request (`pages-the-store-is-read-through-one-seam`).
    /// </remarks>
    internal static async Task<PageTemplate> StoreRevisionAsync(
        StorageDbContext database,
        PageTemplate template,
        ILogger logger,
        CancellationToken cancellationToken) {
        PageTemplate revision = template;
        for (int attempt = 0; ; attempt++) {
            try {
                await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                await WriteRevisionAsync(database, revision, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return revision;
            } catch (DbUpdateException exception) when (attempt + 1 < SqliteErrors.RetryCount && SqliteErrors.IsUniqueViolation(exception)) {
                // Expected and retried: another writer took the revision id between the read and the
                // write. Debug because it is the store being correct, not a failure.
                logger.LogDebug("A concurrent write took the revision id for {Path}; saving it again", revision.Path.Key);
                revision = RegenerateRevision(revision);
            }
        }
    }

    /// <summary>Inserts one revision and points the page's head at it.</summary>
    /// <param name="database">The context to write through, which may be inside a caller's transaction.</param>
    /// <param name="template">The revision to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>
    /// An editor save and an imported batch go through this pair, so the row shape and the head
    /// upsert cannot drift. The revision insert comes first: the head's foreign key points at it.
    /// </remarks>
    internal static async Task WriteRevisionAsync(StorageDbContext database, PageTemplate template, CancellationToken cancellationToken) {
        PageRevision row = ToRevision(template);
        try {
            await database.AddAsync(row, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
        } catch {
            // A failed insert must not leave the row tracked, or the next attempt would try to add it twice.
            database.Entry(row).State = EntityState.Detached;
            throw;
        }

        await WriteHeadAsync(database, template, cancellationToken);
    }

    /// <summary>Points a page's head at an already stored revision, clearing a tombstone.</summary>
    /// <param name="database">The context to write through.</param>
    /// <param name="template">The revision the head should point at.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    internal static Task WriteHeadAsync(StorageDbContext database, PageTemplate template, CancellationToken cancellationToken) {
        string contentHash = TemplateFingerprint.Of(template.Template);

        return database.Database.ExecuteSqlAsync($"""
            INSERT INTO "PageHeads" ("Path", "RevisionId", "IsDeleted", "SeedHash", "ContentHash")
            VALUES ({template.Path.Key}, {template.RevisionId}, 0, {template.SeedHash}, {contentHash})
            ON CONFLICT ("Path") DO UPDATE SET
                "RevisionId" = excluded."RevisionId",
                "IsDeleted" = 0,
                "SeedHash" = COALESCE(excluded."SeedHash", "PageHeads"."SeedHash"),
                "ContentHash" = excluded."ContentHash"
            """, cancellationToken);
    }

    /// <summary>Gives a batch a fresh revision id and hash after a collision.</summary>
    /// <param name="template">The revision that collided.</param>
    internal static PageTemplate RegenerateRevision(PageTemplate template) {
        string revisionId = Guid.NewGuid().ToString("N");
        return template with {
            RevisionId = revisionId,
            Hash = RevisionHash.Of(template.Template, template.CreationRuntimeId, template.SavedAt, revisionId),
        };
    }

    public async Task<bool> AdoptSeedAsync(PagePath path, string seedHash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedHash);

        int adopted = await database.Database.ExecuteSqlAsync($"""
            UPDATE "PageHeads" SET "SeedHash" = {seedHash}
            WHERE "Path" = {path.Key} AND "IsDeleted" = 0 AND "ContentHash" = {seedHash}
            """, cancellationToken);

        return adopted > 0;
    }

    public Task<bool> TrySavePageAsync(PageTemplate template, string expectedHash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        return TryStoreRevisionAsync(database, template, expectedHash, logger, cancellationToken);
    }

    /// <summary>Stores a revision only while the page's head still has the hash the caller saw.</summary>
    /// <param name="database">The context to write through.</param>
    /// <param name="template">The revision to store.</param>
    /// <param name="expectedHash">The hash the caller saw as the head.</param>
    /// <param name="logger">Notes a retried revision id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <remarks>
    /// The editor's conditional save, a page's own conditional save and the assistant all come
    /// through here, whether the context belongs to a request or to a singleton's save.
    /// </remarks>
    internal static async Task<bool> TryStoreRevisionAsync(
        StorageDbContext database,
        PageTemplate template,
        string expectedHash,
        ILogger logger,
        CancellationToken cancellationToken) {
        PageTemplate revision = template;
        for (int attempt = 0; ; attempt++) {
            PageRevision row = ToRevision(revision);
            try {
                await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                int matched = await database.Database.ExecuteSqlAsync($"""
                    UPDATE "PageHeads" SET "RevisionId" = "RevisionId"
                    WHERE "Path" = {revision.Path.Key} AND "IsDeleted" = 0
                        AND EXISTS (
                            SELECT 1 FROM "PageRevisions" AS revision
                            WHERE revision."RevisionId" = "PageHeads"."RevisionId"
                                AND revision."Path" = "PageHeads"."Path"
                                AND revision."Hash" = {expectedHash})
                    """, cancellationToken);
                if (matched == 0) {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                database.Add(row);
                await database.SaveChangesAsync(cancellationToken);
                await database.Database.ExecuteSqlAsync($"""
                    UPDATE "PageHeads" SET
                        "RevisionId" = {revision.RevisionId},
                        "SeedHash" = COALESCE({revision.SeedHash}, "PageHeads"."SeedHash"),
                        "ContentHash" = {TemplateFingerprint.Of(revision.Template)}
                    WHERE "Path" = {revision.Path.Key} AND "IsDeleted" = 0
                    """, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            } catch (DbUpdateException exception) when (attempt + 1 < SqliteErrors.RetryCount && SqliteErrors.IsUniqueViolation(exception)) {
                logger.LogDebug("A concurrent write took the revision id for {Path}; saving it again", revision.Path.Key);
                database.Entry(row).State = EntityState.Detached;
                string revisionId = Guid.NewGuid().ToString("N");
                revision = revision with {
                    RevisionId = revisionId,
                    Hash = RevisionHash.Of(revision.Template, revision.CreationRuntimeId, revision.SavedAt, revisionId),
                };
            }
        }
    }

    public async Task<bool> TrySetImageAsync(string revisionId, string expectedTemplate, PageImage image, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(revisionId);
        ArgumentNullException.ThrowIfNull(expectedTemplate);
        ArgumentNullException.ThrowIfNull(image);

        int updated = await database.Set<PageRevision>()
            .Where(row => row.RevisionId == revisionId && row.Template == expectedTemplate)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.TemplateKind, image.Kind)
                .SetProperty(row => row.TemplateLibraries, image.Libraries)
                .SetProperty(row => row.TemplateHash, image.TemplateHash)
                .SetProperty(row => row.RuntimeId, image.RuntimeId)
                .SetProperty(row => row.Image, image.Bytes)
                .SetProperty(row => row.Symbols, image.Symbols)
                .SetProperty(row => row.Diagnostics, SerializeDiagnostics(image.Diagnostics)), cancellationToken);

        return updated > 0;
    }

    public async Task<bool> DeletePageAsync(PagePath path, CancellationToken cancellationToken = default) {
        bool deleted;
        await using (IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken)) {
            int updated = await database.Set<PageHead>()
                .Where(head => head.Path == path.Key && !head.IsDeleted)
                .ExecuteUpdateAsync(setters => setters.SetProperty(head => head.IsDeleted, true), cancellationToken);

            // State is live data, not history: a new page saved at the same path must not inherit the
            // state of the page that was deleted before it.
            if (updated > 0) {
                await database.Set<PageStateEntry>()
                    .Where(row => row.PagePath == path.Key)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            deleted = updated > 0;
            await transaction.CommitAsync(cancellationToken);
        }

        if (deleted) {
            storageRefresh?.Request();
        }

        return deleted;
    }

    public async Task<PageMoveStoreResult> MovePageAsync(
        PagePath from,
        PagePath to,
        string expectedHash,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var source = await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == from.Key && !head.IsDeleted)
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (head, revision) => new {
                    head.RevisionId,
                    head.SeedHash,
                    head.ContentHash,
                    revision.Hash,
                    revision.TemplateKind,
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (source is null) {
            return PageMoveStoreResult.Of(PageMoveStoreOutcome.SourceNotFound);
        }

        if (source.TemplateKind == PageKind.Library) {
            return PageMoveStoreResult.Of(PageMoveStoreOutcome.IsLibrary);
        }

        if (!string.Equals(source.Hash, expectedHash, StringComparison.Ordinal)) {
            return PageMoveStoreResult.Of(PageMoveStoreOutcome.Stale);
        }

        PageHead? target = await database.Set<PageHead>().AsNoTracking()
            .SingleOrDefaultAsync(head => head.Path == to.Key, cancellationToken);
        if (target is { IsDeleted: false }) {
            return PageMoveStoreResult.Of(PageMoveStoreOutcome.TargetExists);
        }

        // The store is a tree, so a key with anything below it names a folder; moving it would leave
        // that subtree under a key with no page. One read of every head is enough for a store this size.
        string[] heads = await database.Set<PageHead>().AsNoTracking()
            .Select(head => head.Path)
            .ToArrayAsync(cancellationToken);
        IReadOnlyList<string> below = [.. heads
            .Select(PagePath.Normalize)
            .Where(key => key.IsBelow(from))
            .Select(key => key.Key)
            .OrderBy(key => key, StringComparer.Ordinal)];

        if (below.Count > 0) {
            return new PageMoveStoreResult(PageMoveStoreOutcome.FolderNotEmpty, Below: below);
        }

        if (target is not null) {
            // Only a tombstone can reach here: a live target was refused above. Its revisions stay,
            // because history survives a delete; its state belonged to the dead page and does not.
            await database.Database.ExecuteSqlAsync(
                $"""DELETE FROM "PageHeads" WHERE "Path" = {to.Key}""", cancellationToken);
            await database.Database.ExecuteSqlAsync(
                $"""DELETE FROM "PageState" WHERE "PagePath" = {to.Key}""", cancellationToken);
        }

        // The head has to go before its revisions change path: the composite foreign key points at a
        // revision of the head's own path, and it is checked on every row.
        int removed = await database.Database.ExecuteSqlAsync($"""
            DELETE FROM "PageHeads"
            WHERE "Path" = {from.Key} AND "IsDeleted" = 0 AND "RevisionId" = {source.RevisionId}
            """, cancellationToken);
        if (removed == 0) {
            // A save landed between the read and this statement. The transaction rolls back, so the
            // author's edit survives and the caller is told to read the page again.
            await transaction.RollbackAsync(cancellationToken);
            return PageMoveStoreResult.Of(PageMoveStoreOutcome.Stale);
        }

        await database.Database.ExecuteSqlAsync($"""
            UPDATE "PageRevisions" SET "Path" = {to.Key} WHERE "Path" = {from.Key}
            """, cancellationToken);
        await database.Database.ExecuteSqlAsync($"""
            UPDATE "PageState" SET "PagePath" = {to.Key} WHERE "PagePath" = {from.Key}
            """, cancellationToken);
        await database.Database.ExecuteSqlAsync($"""
            INSERT INTO "PageHeads" ("Path", "RevisionId", "IsDeleted", "SeedHash", "ContentHash")
            VALUES ({to.Key}, {source.RevisionId}, 0, {source.SeedHash}, {source.ContentHash})
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        storageRefresh?.Request();

        return new PageMoveStoreResult(PageMoveStoreOutcome.Moved, source.Hash);
    }

    public async Task<bool> ScrubPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        PageScrubResult result = await ScrubStoredPathAsync(path, requireTombstone: true, cancellationToken);
        return result.Head;
    }

    public Task<PageScrubResult> ScrubStoredPathAsync(PagePath path, CancellationToken cancellationToken = default) =>
        ScrubStoredPathAsync(path, requireTombstone: false, cancellationToken);

    private async Task<PageScrubResult> ScrubStoredPathAsync(
        PagePath path,
        bool requireTombstone,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(path);

        await database.Database.OpenConnectionAsync(cancellationToken);
        try {
            await ExecuteNonQueryAsync("PRAGMA secure_delete=ON", cancellationToken);

            PageScrubResult removed;
            await using (IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken)) {
                int heads = await database.Set<PageHead>()
                    .Where(head => head.Path == path.Key && (!requireTombstone || head.IsDeleted))
                    .ExecuteDeleteAsync(cancellationToken);

                int state = 0;
                int revisions = 0;
                if (heads > 0 || !requireTombstone) {
                    // Only when a tombstoned head was actually removed, so a database written by an
                    // older build cannot leave orphaned rows behind. The repair has no head to
                    // require: a row at a shell path can never be deleted through a page.
                    state = await database.Set<PageStateEntry>()
                        .Where(row => row.PagePath == path.Key)
                        .ExecuteDeleteAsync(cancellationToken);
                    revisions = await database.Set<PageRevision>()
                        .Where(revision => revision.Path == path.Key)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                removed = new PageScrubResult(heads > 0, revisions, state);
                await transaction.CommitAsync(cancellationToken);
            }

            await CheckpointWalAsync(cancellationToken);
            return removed;
        } finally {
            await database.Database.CloseConnectionAsync();
            storageRefresh?.Request();
        }
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken) {
        await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Every caller passes a constant statement and binds its values.
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CheckpointWalAsync(CancellationToken cancellationToken) {
        string? journalMode = await ExecuteScalarAsync("PRAGMA journal_mode", cancellationToken) as string;
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        for (int attempt = 0; attempt < CheckpointAttempts; attempt++) {
            await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) {
                throw new InvalidOperationException("SQLite did not report the WAL checkpoint result.");
            }

            int busy = reader.GetInt32(0);
            int logFrames = reader.GetInt32(1);
            int checkpointedFrames = reader.GetInt32(2);
            if (busy == 0 && (logFrames < 0 || checkpointedFrames >= logFrames)) {
                return;
            }

            if (attempt + 1 < CheckpointAttempts) {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
            }
        }

        throw new IOException("SQLite could not truncate the WAL because an active reader is using it.");
    }

    private async Task<object?> ExecuteScalarAsync(string sql, CancellationToken cancellationToken) {
        await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // Every caller passes a constant statement and binds its values.
        command.CommandText = sql;
#pragma warning restore CA2100
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private PageTemplate ToPage(PageRevision revision) {
        return new PageTemplate(PagePath.Normalize(revision.Path), revision.Template, ToImage(revision)) {
            RevisionId = revision.RevisionId,
            Hash = revision.Hash,
            CreationRuntimeId = revision.CreationRuntimeId,
            SavedAt = revision.SavedAt,
            Kind = revision.TemplateKind,
        };
    }

    private static PageRevision ToRevision(PageTemplate template) {
        PageImage? image = template.Image;
        return new PageRevision {
            RevisionId = template.RevisionId,
            Path = template.Path.Key,
            Hash = template.Hash,
            SavedAt = template.SavedAt,
            Template = template.Template,
            CreationRuntimeId = template.CreationRuntimeId,
            TemplateKind = template.Kind ?? image?.Kind,
            TemplateLibraries = image?.Libraries,
            TemplateHash = image?.TemplateHash,
            RuntimeId = image?.RuntimeId,
            Image = image?.Bytes,
            Symbols = image?.Symbols,
            Diagnostics = image is null ? null : SerializeDiagnostics(image.Diagnostics),
        };
    }

    private PageImage? ToImage(PageRevision revision) {
        if (revision.RuntimeId is null) {
            return null;
        }

        return new PageImage(
            revision.TemplateHash ?? string.Empty,
            revision.RuntimeId,
            revision.Image,
            revision.Symbols,
            Deserialize(revision.Diagnostics, revision.Image is not null),
            revision.TemplateKind,
            revision.TemplateLibraries);
    }

    private IReadOnlyList<PageDiagnostic> Deserialize(string? json, bool hasImage) {
        if (json is null) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<PageDiagnostic[]>(json, s_jsonOptions) ?? [];
        } catch (JsonException exception) {
            // Rows written before diagnostics carried a severity stored a plain string array. The
            // image is the only clue to what they meant: no image means they were errors. Debug
            // rather than a warning: reading an old row is the fallback working, not a fault.
            logger.LogDebug(exception, "A stored diagnostics column is in the pre-severity format and is being read as one");
            string[]? legacy = JsonSerializer.Deserialize<string[]>(json);
            if (legacy is null) {
                return [];
            }

            PageDiagnosticSeverity severity = hasImage
                ? PageDiagnosticSeverity.Warning
                : PageDiagnosticSeverity.Error;
            return [.. legacy.Select(message => new PageDiagnostic(severity, null, message))];
        }
    }

    private static string SerializeDiagnostics(IReadOnlyList<PageDiagnostic> diagnostics) {
        return JsonSerializer.Serialize(diagnostics, s_jsonOptions);
    }
}
