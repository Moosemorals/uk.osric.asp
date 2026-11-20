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

                // In the order the page staged them, each operation proving itself against what the
                // ones before it have written: the transaction's state is what a later operation sees.
                List<PageCommitAnswer> answers = [];
                PageCommitAnswer? refused = null;
                foreach (PageCommit operation in batch) {
                    PageCommitAnswer answer = await ApplyAsync(database, operation, cancellationToken);
                    answers.Add(answer);

                    if (IsRefusal(answer.Outcome)) {
                        refused = answer;
                        break;
                    }
                }

                if (refused is not null) {
                    await transaction.RollbackAsync(cancellationToken);
                    return Report(batch, answers);
                }

                await transaction.CommitAsync(cancellationToken);
                refresh?.Request();

                return answers;
            } catch (DbUpdateException exception) when (attempt + 1 < SqliteErrors.RetryCount && SqliteErrors.IsUniqueViolation(exception)) {
                // The one failure an ordinary save already retries: another writer took a revision id
                // between the hash being computed and the row being inserted. Everything rolled back,
                // so the whole commit is tried again with fresh ids.
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

    // A refusal writes nothing, so the operations before it were applied and undone and the ones after
    // it were never reached. The report says which is which rather than calling them all the same.
    private static PageCommitAnswer[] Report(IReadOnlyList<PageCommit> batch, List<PageCommitAnswer> attempts) {
        PageCommitAnswer[] rows = new PageCommitAnswer[batch.Count];

        for (int index = 0; index < batch.Count; index++) {
            rows[index] = index < attempts.Count
                ? attempts[index] with {
                    Outcome = attempts[index].Outcome == PageCommitOutcome.Unchanged
                        ? PageCommitOutcome.Unchanged
                        : index == attempts.Count - 1 ? attempts[index].Outcome : PageCommitOutcome.RolledBack,
                }
                : new PageCommitAnswer(batch[index].Key, PageCommitOutcome.NotAttempted);
        }

        return rows;
    }

    // Unchanged is not a refusal: the page already held what the operation would have written.
    private static bool IsRefusal(PageCommitOutcome outcome) =>
        outcome is PageCommitOutcome.Stale
            or PageCommitOutcome.SourceNotFound
            or PageCommitOutcome.TargetExists
            or PageCommitOutcome.FolderNotEmpty
            or PageCommitOutcome.Refused;

    /// <summary>Applies one operation, proving it against the transaction's current state.</summary>
    private static async Task<PageCommitAnswer> ApplyAsync(
        StorageDbContext database,
        PageCommit operation,
        CancellationToken cancellationToken) => operation.Kind switch {
            PageCommitKind.Put or PageCommitKind.Save or PageCommitKind.Create => await SaveAsync(database, operation, cancellationToken),
            PageCommitKind.Move => await MoveAsync(database, operation, cancellationToken),
            PageCommitKind.Delete => await DeleteAsync(database, operation, cancellationToken),
            _ => await AdoptAsync(database, operation, cancellationToken),
        };

    private static async Task<PageCommitAnswer> SaveAsync(
        StorageDbContext database,
        PageCommit operation,
        CancellationToken cancellationToken) {
        if (operation.Revision is not { } revision) {
            throw new InvalidOperationException($"A save of {operation.Key.Key} arrived without a revision.");
        }

        switch (operation.Kind) {
            case PageCommitKind.Create:
                // A create is a save with no expectation: it applies only while the key has no live
                // page, which is the state this transaction has left it in.
                bool live = await database.Set<PageHead>().AsNoTracking()
                    .AnyAsync(head => head.Path == operation.Key.Key && !head.IsDeleted, cancellationToken);
                if (live) {
                    return new(operation.Key, PageCommitOutcome.Stale);
                }

                await SqlitePageStore.WriteRevisionAsync(database, revision, cancellationToken);
                return new(operation.Key, PageCommitOutcome.Saved, revision.Hash, Stored: revision);

            case PageCommitKind.Save:
                bool written = await SqlitePageStore.TryWriteRevisionAsync(
                    database, revision, SqlitePageStore.ToRevision(revision), operation.ExpectedHash ?? string.Empty, cancellationToken);

                return written
                    ? new(operation.Key, PageCommitOutcome.Saved, revision.Hash, Stored: revision)
                    : new(operation.Key, PageCommitOutcome.Stale);

            default:
                await SqlitePageStore.WriteRevisionAsync(database, revision, cancellationToken);
                return new(operation.Key, PageCommitOutcome.Saved, revision.Hash, Stored: revision);
        }
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
