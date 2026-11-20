// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The <see cref="IPageCommitStore"/> over a context factory.</summary>
/// <remarks>
/// <para>
/// One context per commit, created and disposed here, because the commit may run after the request
/// that started it has gone — the same reason the bound self-save opens its own. Revisions go through
/// <see cref="SqlitePageStore.WriteRevisionAsync"/>, a delete through
/// <see cref="SqlitePageStore.DeleteWithinTransactionAsync"/> and a move through
/// <see cref="SqlitePageStore.MoveWithinTransactionAsync"/>, so a commit cannot spell a write
/// differently from the single write of the same thing.
/// </para>
/// <para>
/// Nothing is compiled here: a save arrives already built, and the commit is statements only, which
/// is what keeps the store's one writer held for milliseconds.
/// </para>
/// </remarks>
internal sealed class SqlitePageCommitStore(
    IDbContextFactory<StorageDbContext> factory,
    ILogger<SqlitePageCommitStore> logger,
    PageStorageRefresh? refresh = null) : IPageCommitStore {

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageCommitAnswer>> CommitAsync(
        IReadOnlyList<PageCommit> operations,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(operations);

        if (operations.Count == 0) {
            return [];
        }

        IReadOnlyList<PageCommit> batch = operations;

        for (int attempt = 0; ; attempt++) {
            await using StorageDbContext database = factory.CreateDbContext();

            try {
                await using IDbContextTransaction transaction = await database.Database.BeginTransactionAsync(cancellationToken);

                PageCommitAnswer[] checked_ = await CheckAsync(database, batch, cancellationToken);
                PageCommitAnswer? refused = checked_.FirstOrDefault(answer => IsRefusal(answer.Outcome));
                if (refused is not null) {
                    // NotAttempted for everything that could have gone ahead, so the report says what
                    // the refusal cost as well as what refused.
                    PageCommitAnswer[] refusedAnswers = [.. checked_.Select(answer => answer.Outcome switch {
                        PageCommitOutcome.Unchanged => answer,
                        _ when answer.Key == refused.Key => answer,
                        _ => answer with { Outcome = PageCommitOutcome.NotAttempted },
                    })];

                    await transaction.RollbackAsync(cancellationToken);
                    return refusedAnswers;
                }

                PageCommitAnswer[] stored = await ApplyAsync(database, batch, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                refresh?.Request();

                return stored;
            } catch (DbUpdateException exception) when (attempt + 1 < SqliteErrors.RetryCount && SqliteErrors.IsUniqueViolation(exception)) {
                // The one failure an ordinary save already retries: another writer took a revision id
                // between the hash being computed and the row being inserted.
                logger.LogDebug("A concurrent write took a revision id in this commit; regenerating its ids and trying again");
                database.ChangeTracker.Clear();
                batch = [.. batch.Select(commit => commit.Revision is { } revision
                    ? commit with { Revision = SqlitePageStore.RegenerateRevision(revision) }
                    : commit)];
            } catch (SqliteException exception) when (SqliteErrors.IsBusy(exception)) {
                // Nothing was written: the transaction is rolled back by SQLite and the page is told
                // the store was busy rather than having its request quietly retried for it.
                logger.LogDebug("The store was busy when a commit of {Count} operation(s) started", batch.Count);
                return [.. batch.Select(commit => new PageCommitAnswer(commit.Key, PageCommitOutcome.Busy))];
            }
        }
    }

    // Unchanged is not a refusal: the page already held what the operation would have written.
    private static bool IsRefusal(PageCommitOutcome outcome) =>
        outcome is PageCommitOutcome.Stale
            or PageCommitOutcome.SourceNotFound
            or PageCommitOutcome.TargetExists
            or PageCommitOutcome.FolderNotEmpty
            or PageCommitOutcome.Refused;

    /// <summary>Proves every operation against the heads it names, writing nothing.</summary>
    private static async Task<PageCommitAnswer[]> CheckAsync(
        StorageDbContext database,
        IReadOnlyList<PageCommit> operations,
        CancellationToken cancellationToken) {
        PageCommitAnswer[] answers = new PageCommitAnswer[operations.Count];

        for (int index = 0; index < operations.Count; index++) {
            PageCommit operation = operations[index];
            answers[index] = await CheckAsync(database, operation, cancellationToken);
        }

        return answers;
    }

    private static async Task<PageCommitAnswer> CheckAsync(
        StorageDbContext database,
        PageCommit operation,
        CancellationToken cancellationToken) {
        if (operation is { Kind: PageCommitKind.Move, From: { } from, Target: { } target }) {
            PageMoveStoreResult? refusal = await SqlitePageStore.CheckMoveAsync(
                database, from, target, operation.ExpectedHash ?? string.Empty, cancellationToken);

            // No refusal is a provisional "it will move"; the write pass is what reports the outcome.
            if (refusal is null) {
                return new(operation.Key, PageCommitOutcome.Moved);
            }

            return refusal.Outcome switch {
                PageMoveStoreOutcome.SourceNotFound => new(operation.Key, PageCommitOutcome.SourceNotFound),
                PageMoveStoreOutcome.Stale => new(operation.Key, PageCommitOutcome.Stale),
                PageMoveStoreOutcome.TargetExists => new(operation.Key, PageCommitOutcome.TargetExists),
                PageMoveStoreOutcome.FolderNotEmpty => new(operation.Key, PageCommitOutcome.FolderNotEmpty, Below: refusal.Below),
                PageMoveStoreOutcome.IsLibrary => throw new InvalidOperationException(
                    $"{from.Key} is a library: its key is in every dependent's closure, so moving it would leave those pages naming an empty key."),
                _ => new(operation.Key, PageCommitOutcome.Stale),
            };
        }

        PageHead? head = await database.Set<PageHead>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Path == operation.Key.Key, cancellationToken);

        switch (operation.Kind) {
            case PageCommitKind.Create when head is { IsDeleted: false }:
                return new(operation.Key, PageCommitOutcome.Stale);
            case PageCommitKind.Save:
                if (head is null || head.IsDeleted) {
                    return new(operation.Key, PageCommitOutcome.Stale);
                }

                bool matched = await database.Set<PageHead>().AsNoTracking()
                    .Where(row => row.Path == operation.Key.Key && !row.IsDeleted)
                    .Join(database.Set<PageRevision>().AsNoTracking(),
                        row => new { row.RevisionId, row.Path },
                        revision => new { revision.RevisionId, revision.Path },
                        (_, revision) => revision.Hash)
                    .AnyAsync(hash => hash == operation.ExpectedHash, cancellationToken);

                return matched
                    ? new(operation.Key, PageCommitOutcome.Saved)
                    : new(operation.Key, PageCommitOutcome.Stale);
            case PageCommitKind.Put:
                return new(operation.Key, PageCommitOutcome.Saved);
            case PageCommitKind.Delete:
                return head is { IsDeleted: false }
                    ? new(operation.Key, PageCommitOutcome.Deleted)
                    : new(operation.Key, PageCommitOutcome.SourceNotFound);
            case PageCommitKind.Adopt:
                if (head is not { IsDeleted: false }) {
                    return new(operation.Key, PageCommitOutcome.SourceNotFound);
                }

                if (string.Equals(head.SeedHash, operation.SeedHash, StringComparison.Ordinal)) {
                    return new(operation.Key, PageCommitOutcome.Unchanged);
                }

                return string.Equals(head.ContentHash, operation.SeedHash, StringComparison.Ordinal)
                    ? new(operation.Key, PageCommitOutcome.Saved)
                    : new(operation.Key, PageCommitOutcome.Refused);
            default:
                return new(operation.Key, PageCommitOutcome.Saved);
        }
    }

    /// <summary>Writes every operation, now that all of them are proved.</summary>
    private static async Task<PageCommitAnswer[]> ApplyAsync(
        StorageDbContext database,
        IReadOnlyList<PageCommit> operations,
        CancellationToken cancellationToken) {
        PageCommitAnswer[] answers = new PageCommitAnswer[operations.Count];

        for (int index = 0; index < operations.Count; index++) {
            PageCommit operation = operations[index];
            answers[index] = operation.Kind switch {
                PageCommitKind.Save or PageCommitKind.Create or PageCommitKind.Put => await SaveAsync(database, operation, cancellationToken),
                PageCommitKind.Move => await MoveAsync(database, operation, cancellationToken),
                PageCommitKind.Delete => await DeleteAsync(database, operation, cancellationToken),
                _ => await AdoptAsync(database, operation, cancellationToken),
            };
        }

        return answers;
    }

    private static async Task<PageCommitAnswer> SaveAsync(
        StorageDbContext database,
        PageCommit operation,
        CancellationToken cancellationToken) {
        if (operation.Revision is not { } revision) {
            throw new InvalidOperationException($"A save of {operation.Key.Key} arrived without a revision.");
        }

        await SqlitePageStore.WriteRevisionAsync(database, revision, cancellationToken);

        return new(operation.Key, PageCommitOutcome.Saved, revision.Hash, Stored: revision);
    }

    private static async Task<PageCommitAnswer> MoveAsync(
        StorageDbContext database,
        PageCommit operation,
        CancellationToken cancellationToken) {
        if (operation is not { From: { } from, Target: { } target }) {
            throw new InvalidOperationException($"A move of {operation.Key.Key} arrived without both keys.");
        }

        PageMoveStoreResult moved = await SqlitePageStore.MoveWithinTransactionAsync(
            database, from, target, operation.ExpectedHash ?? string.Empty, cancellationToken);

        return moved.Outcome == PageMoveStoreOutcome.Moved
            ? new(operation.Key, PageCommitOutcome.Moved, moved.Hash)
            : new(operation.Key, PageCommitOutcome.Stale);
    }

    private static async Task<PageCommitAnswer> DeleteAsync(
        StorageDbContext database,
        PageCommit operation,
        CancellationToken cancellationToken) {
        bool deleted = await SqlitePageStore.DeleteWithinTransactionAsync(database, operation.Key, cancellationToken);

        return new(operation.Key, deleted ? PageCommitOutcome.Deleted : PageCommitOutcome.SourceNotFound);
    }

    private static async Task<PageCommitAnswer> AdoptAsync(
        StorageDbContext database,
        PageCommit operation,
        CancellationToken cancellationToken) {
        int adopted = await database.Database.ExecuteSqlAsync($"""
            UPDATE "PageHeads" SET "SeedHash" = {operation.SeedHash}
            WHERE "Path" = {operation.Key.Key} AND "IsDeleted" = 0 AND "ContentHash" = {operation.SeedHash}
            """, cancellationToken);

        return new(operation.Key, adopted > 0 ? PageCommitOutcome.Saved : PageCommitOutcome.Refused);
    }
}
