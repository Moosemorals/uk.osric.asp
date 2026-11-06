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

internal sealed class SqlitePageStore(StorageDbContext database, PageStorageRefresh? storageRefresh = null) : IPageStore {

    private const int IdentityRetryCount = 3;
    private const int CheckpointAttempts = 3;

    private static readonly JsonSerializerOptions s_jsonOptions = new() {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<StoredPage?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        StoredRevision? revision = await database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == path.Key && !head.IsDeleted)
            .Join(database.Set<StoredRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                row => new { row.RevisionId, row.Path },
                (_, row) => row)
            .SingleOrDefaultAsync(cancellationToken);

        return revision is null ? null : ToPage(revision);
    }

    public async Task<IReadOnlyList<PageHeadSummary>> ListPagesAsync(CancellationToken cancellationToken = default) {
        var rows = await database.Set<PageHead>().AsNoTracking()
            .Select(head => new { head.Path, head.IsDeleted })
            .ToArrayAsync(cancellationToken);

        return [.. rows.OrderBy(row => row.Path, StringComparer.Ordinal)
            .Select(row => new PageHeadSummary(PagePath.Normalize(row.Path), row.IsDeleted))];
    }

    public async Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default) {
        PageHead? head = await database.Set<PageHead>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Path == path.Key, cancellationToken);

        return head is null ? null : new PageHeadSummary(PagePath.Normalize(head.Path), head.IsDeleted);
    }

    public async Task<StoredPage?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(hash);

        StoredRevision? revision = await database.Set<StoredRevision>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Hash == hash, cancellationToken);

        return revision is null ? null : ToPage(revision);
    }

    public async Task<IReadOnlyList<PageRevisionSummary>> ListRevisionsAsync(PagePath path, CancellationToken cancellationToken = default) {
        var rows = await database.Set<StoredRevision>().AsNoTracking()
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
                CompilationStatus(row.RuntimeId, row.HasImage, row.HasDiagnostics),
                row.IsCurrent))];
    }

    public async Task<PageRevisionSource?> GetRevisionSourceByHashAsync(string hash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(hash);

        var revision = await database.Set<StoredRevision>().AsNoTracking()
            .Where(row => row.Hash == hash)
            .Select(row => new {
                row.Path,
                row.Hash,
                row.SavedAt,
                row.CreationRuntimeId,
                row.RuntimeId,
                HasImage = row.Image != null,
                HasDiagnostics = row.Diagnostics != null && row.Diagnostics != "[]",
                row.Content,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return revision is null
            ? null
            : new PageRevisionSource(
                PagePath.Normalize(revision.Path),
                revision.Hash,
                revision.SavedAt,
                revision.CreationRuntimeId,
                CompilationStatus(revision.RuntimeId, revision.HasImage, revision.HasDiagnostics),
                revision.Content);
    }

    public async Task SavePageAsync(StoredPage page, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);

        StoredPage revision = page;
        for (int attempt = 0; ; attempt++) {
            StoredRevision row = ToStoredRevision(revision);
            try {
                await using (IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken)) {
                    database.Add(row);
                    await database.SaveChangesAsync(cancellationToken);
                    await database.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "PageHeads" ("Path", "RevisionId", "IsDeleted")
                        VALUES ({revision.Path.Key}, {revision.RevisionId}, 0)
                        ON CONFLICT ("Path") DO UPDATE SET
                            "RevisionId" = excluded."RevisionId",
                            "IsDeleted" = 0
                        """, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }

                return;
            } catch (DbUpdateException exception) when (attempt + 1 < IdentityRetryCount && IsIdentityCollision(exception)) {
                database.Entry(row).State = EntityState.Detached;
                string revisionId = Guid.NewGuid().ToString("N");
                revision = revision with {
                    RevisionId = revisionId,
                    Hash = PageRevisionHash.Compute(revision.Content, revision.CreationRuntimeId, revision.SavedAt, revisionId),
                };
            }
        }
    }

    public async Task<bool> TrySavePageAsync(StoredPage page, string expectedHash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        StoredPage revision = page;
        for (int attempt = 0; ; attempt++) {
            StoredRevision row = ToStoredRevision(revision);
            try {
                await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);
                int matched = await database.Database.ExecuteSqlInterpolatedAsync($"""
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
                await database.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "PageHeads" SET "RevisionId" = {revision.RevisionId}
                    WHERE "Path" = {revision.Path.Key} AND "IsDeleted" = 0
                    """, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            } catch (DbUpdateException exception) when (attempt + 1 < IdentityRetryCount && IsIdentityCollision(exception)) {
                database.Entry(row).State = EntityState.Detached;
                string revisionId = Guid.NewGuid().ToString("N");
                revision = revision with {
                    RevisionId = revisionId,
                    Hash = PageRevisionHash.Compute(revision.Content, revision.CreationRuntimeId, revision.SavedAt, revisionId),
                };
            }
        }
    }

    public async Task<bool> TrySetCompilationAsync(string revisionId, string expectedContent, PageCompilation compilation, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(revisionId);
        ArgumentNullException.ThrowIfNull(expectedContent);
        ArgumentNullException.ThrowIfNull(compilation);

        int updated = await database.Set<StoredRevision>()
            .Where(row => row.RevisionId == revisionId && row.Content == expectedContent)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.ContentHash, compilation.ContentHash)
                .SetProperty(row => row.RuntimeId, compilation.RuntimeId)
                .SetProperty(row => row.Image, compilation.Image)
                .SetProperty(row => row.Symbols, compilation.Symbols)
                .SetProperty(row => row.Diagnostics, SerializeDiagnostics(compilation.Diagnostics)), cancellationToken);

        return updated > 0;
    }

    public async Task<bool> DeletePageAsync(PagePath path, CancellationToken cancellationToken = default) {
        int updated = await database.Set<PageHead>()
            .Where(head => head.Path == path.Key && !head.IsDeleted)
            .ExecuteUpdateAsync(setters => setters.SetProperty(head => head.IsDeleted, true), cancellationToken);

        return updated > 0;
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
                    await database.Set<StoredRevision>()
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

    private static StoredPage ToPage(StoredRevision revision) {
        return new StoredPage(PagePath.Normalize(revision.Path), revision.Content, ToCompilation(revision)) {
            RevisionId = revision.RevisionId,
            Hash = revision.Hash,
            CreationRuntimeId = revision.CreationRuntimeId,
            SavedAt = revision.SavedAt,
        };
    }

    private static StoredRevision ToStoredRevision(StoredPage page) {
        PageCompilation? compilation = page.Compilation;
        return new StoredRevision {
            RevisionId = page.RevisionId,
            Path = page.Path.Key,
            Hash = page.Hash,
            SavedAt = page.SavedAt,
            Content = page.Content,
            CreationRuntimeId = page.CreationRuntimeId,
            ContentHash = compilation?.ContentHash,
            RuntimeId = compilation?.RuntimeId,
            Image = compilation?.Image,
            Symbols = compilation?.Symbols,
            Diagnostics = compilation is null ? null : SerializeDiagnostics(compilation.Diagnostics),
        };
    }

    private static PageCompilation? ToCompilation(StoredRevision revision) {
        if (revision.RuntimeId is null) {
            return null;
        }

        return new PageCompilation(
            revision.ContentHash ?? string.Empty,
            revision.RuntimeId,
            revision.Image,
            revision.Symbols,
            Deserialize(revision.Diagnostics, revision.Image is not null));
    }

    private static bool IsIdentityCollision(DbUpdateException exception) {
        return exception.InnerException is SqliteException {
            SqliteExtendedErrorCode: 1555 or 2067,
        };
    }

    private static string CompilationStatus(string? runtimeId, bool hasImage, bool hasDiagnostics) {
        if (runtimeId is null) {
            return "Not compiled";
        }

        if (!hasImage) {
            return "Compile failed";
        }

        return hasDiagnostics ? "Compiled with diagnostics" : "Compiled";
    }

    private static IReadOnlyList<PageDiagnostic> Deserialize(string? json, bool hasImage) {
        if (json is null) {
            return [];
        }

        try {
            return JsonSerializer.Deserialize<PageDiagnostic[]>(json, s_jsonOptions) ?? [];
        } catch (JsonException) {
            // Rows written before diagnostics carried a severity stored a plain string array. The
            // image is the only clue to what they meant: no image means they were errors.
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
