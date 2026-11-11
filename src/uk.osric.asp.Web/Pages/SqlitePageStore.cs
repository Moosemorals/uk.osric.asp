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
using uk.osric.asp.Web.Storage;

/// <summary>The SQLite-backed page store.</summary>
internal sealed class SqlitePageStore(
    StorageDbContext database,
    ILogger<SqlitePageStore> logger,
    PageStorageRefresh? storageRefresh = null,
    string? currentRuntimeId = null) : IPageStore {

    private const int CheckpointAttempts = 3;

    private static readonly JsonSerializerOptions s_jsonOptions = new() {
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

    public async Task<IReadOnlyList<PageListing>> ListPagesAsync(CancellationToken cancellationToken = default) {
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
                    HasImage = revision.Image != null,
                    HasDiagnostics = revision.Diagnostics != null && revision.Diagnostics != "[]",
                })
            .ToArrayAsync(cancellationToken);

        return [.. rows.OrderBy(row => row.Path, StringComparer.Ordinal)
            .Select(row => new PageListing(
                PagePath.Normalize(row.Path),
                row.IsDeleted,
                PageImageStatus.Describe(row.RuntimeId, row.HasImage, row.HasDiagnostics, currentRuntimeId)))];
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

        return head is null ? null : new PageHeadSummary(PagePath.Normalize(head.Path), head.IsDeleted);
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
                PageImageStatus.Describe(row.RuntimeId, row.HasImage, row.HasDiagnostics),
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
                PageImageStatus.Describe(revision.RuntimeId, revision.HasImage, revision.HasDiagnostics),
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
    /// transaction, the row shape and the retry cannot drift apart (DEC-192). The single save passes
    /// the request's context; a page's save opens one of its own, because its task can outlive the
    /// request (DEC-172).
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
        return database.Database.ExecuteSqlAsync($"""
            INSERT INTO "PageHeads" ("Path", "RevisionId", "IsDeleted")
            VALUES ({template.Path.Key}, {template.RevisionId}, 0)
            ON CONFLICT ("Path") DO UPDATE SET
                "RevisionId" = excluded."RevisionId",
                "IsDeleted" = 0
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

    public async Task<bool> TrySavePageAsync(PageTemplate template, string expectedHash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

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
                    UPDATE "PageHeads" SET "RevisionId" = {revision.RevisionId}
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

    public async Task<bool> ScrubPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        await database.Database.OpenConnectionAsync(cancellationToken);
        try {
            await ExecuteNonQueryAsync("PRAGMA secure_delete=ON", cancellationToken);

            bool existed;
            await using (IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken)) {
                int deletedHeads = await database.Set<PageHead>()
                    .Where(head => head.Path == path.Key && head.IsDeleted)
                    .ExecuteDeleteAsync(cancellationToken);
                if (deletedHeads > 0) {
                    // Only when a tombstoned head was actually removed, so a database written by an
                    // older build cannot leave orphaned rows behind.
                    await database.Set<PageStateEntry>()
                        .Where(row => row.PagePath == path.Key)
                        .ExecuteDeleteAsync(cancellationToken);
                    await database.Set<PageRevision>()
                        .Where(revision => revision.Path == path.Key)
                        .ExecuteDeleteAsync(cancellationToken);
                }
                existed = deletedHeads > 0;
                await transaction.CommitAsync(cancellationToken);
            }

            await CheckpointWalAsync(cancellationToken);
            return existed;
        } finally {
            await database.Database.CloseConnectionAsync();
            storageRefresh?.Request();
        }
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken) {
        await using DbCommand command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
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
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private PageTemplate ToPage(PageRevision revision) {
        return new PageTemplate(PagePath.Normalize(revision.Path), revision.Template, ToImage(revision)) {
            RevisionId = revision.RevisionId,
            Hash = revision.Hash,
            CreationRuntimeId = revision.CreationRuntimeId,
            SavedAt = revision.SavedAt,
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
            Deserialize(revision.Diagnostics, revision.Image is not null));
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
