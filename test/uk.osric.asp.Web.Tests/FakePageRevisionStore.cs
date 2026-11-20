// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IPageRevisionStore"/> that records what a save asked of it.</summary>
/// <remarks>
/// It writes into the fixture's <see cref="FakePageStore"/>, so a test can save through the saver and
/// read the result back through the store it already has. The real seam, which opens its own context,
/// is proven separately in <see cref="SqlitePageRevisionStoreTests"/>.
/// </remarks>
internal sealed class FakePageRevisionStore(FakePageStore pages) : IPageRevisionStore {

    /// <summary>What the store should throw instead of storing anything.</summary>
    public Exception? Failure { get; init; }

    public int SaveCount { get; private set; }

    /// <summary>Every revision this store was asked to store, in order.</summary>
    public List<PageTemplate> Saved { get; } = [];

    public PageTemplate? Last => Saved.Count == 0 ? null : Saved[^1];

    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>Runs once, before a revision is stored, so a test can race a save.</summary>
    public Action<PageTemplate>? OnSave { get; set; }

    /// <inheritdoc/>
    public async Task<PageTemplate> SaveAsync(PageTemplate revision, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(revision);

        if (Failure is { } failure) {
            throw failure;
        }

        if (OnSave is { } onSave) {
            OnSave = null;
            onSave(revision);
        }

        SaveCount++;
        LastCancellationToken = cancellationToken;
        Saved.Add(revision);

        await pages.SavePageAsync(revision, cancellationToken);

        return revision;
    }

    /// <inheritdoc/>
    public async Task<bool> TrySaveAsync(PageTemplate revision, string expectedHash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        if (Failure is { } failure) {
            throw failure;
        }

        if (OnSave is { } onSave) {
            OnSave = null;
            onSave(revision);
        }

        bool saved = await pages.TrySavePageAsync(revision, expectedHash, cancellationToken);
        if (saved) {
            SaveCount++;
            LastCancellationToken = cancellationToken;
            Saved.Add(revision);
        }

        return saved;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageCommitAnswer>> CommitAsync(
        IReadOnlyList<PageCommit> operations,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(operations);

        // The store proves every condition before it writes anything, so the fixture does too: a
        // refusal must be able to report the writes it cost, and nothing may be written first.
        List<PageCommitAnswer> proved = [];
        foreach (PageCommit operation in operations) {
            proved.Add(Check(operation));
        }

        PageCommitAnswer? refused = proved.FirstOrDefault(answer => answer.Outcome is
            PageCommitOutcome.Stale or PageCommitOutcome.SourceNotFound or
            PageCommitOutcome.TargetExists or PageCommitOutcome.FolderNotEmpty or PageCommitOutcome.Refused);

        if (refused is not null) {
            return [.. proved.Select(answer => answer.Outcome is PageCommitOutcome.Unchanged
                ? answer
                : answer.Key == refused.Key ? answer : answer with { Outcome = PageCommitOutcome.NotAttempted })];
        }

        List<PageCommitAnswer> answers = [];
        foreach (PageCommit operation in operations) {
            answers.Add(operation.Kind switch {
                PageCommitKind.Put => await PutAsync(operation, cancellationToken),
                PageCommitKind.Create => await PutAsync(operation, cancellationToken),
                PageCommitKind.Save => await PutAsync(operation, cancellationToken),
                PageCommitKind.Delete => await DeleteAsync(operation, cancellationToken),
                PageCommitKind.Move => await MoveAsync(operation, cancellationToken),
                _ => await pages.AdoptSeedAsync(operation.Key, operation.SeedHash!, cancellationToken)
                    ? new PageCommitAnswer(operation.Key, PageCommitOutcome.Saved)
                    : new PageCommitAnswer(operation.Key, PageCommitOutcome.Refused),
            });
        }

        return answers;
    }

    private PageCommitAnswer Check(PageCommit operation) {
        PageTemplate? head = pages[operation.Key.Key];

        return operation.Kind switch {
            PageCommitKind.Put => new(operation.Key, PageCommitOutcome.Saved),
            PageCommitKind.Create => head is null
                ? new(operation.Key, PageCommitOutcome.Saved)
                : new(operation.Key, PageCommitOutcome.Stale),
            PageCommitKind.Save => head is not null && string.Equals(head.Hash, operation.ExpectedHash, StringComparison.Ordinal)
                ? new(operation.Key, PageCommitOutcome.Saved)
                : new(operation.Key, PageCommitOutcome.Stale),
            PageCommitKind.Delete => head is null
                ? new(operation.Key, PageCommitOutcome.SourceNotFound)
                : new(operation.Key, PageCommitOutcome.Deleted),
            PageCommitKind.Move => CheckMove(operation),
            _ => string.Equals(pages[operation.Key.Key]?.SeedHash, operation.SeedHash, StringComparison.Ordinal)
                ? new(operation.Key, PageCommitOutcome.Unchanged)
                : new(operation.Key, PageCommitOutcome.Saved),
        };
    }

    private PageCommitAnswer CheckMove(PageCommit operation) {
        PageTemplate? source = pages[operation.From!.Key];
        if (source is null) {
            return new PageCommitAnswer(operation.Key, PageCommitOutcome.SourceNotFound);
        }

        if (source.Kind == PageKind.Library || source.Image?.Kind == PageKind.Library) {
            // A library's key is in every dependent's closure, so a move of one is a page bug.
            throw new InvalidOperationException($"{operation.From.Key} is a library.");
        }

        if (pages[operation.Target!.Key] is not null) {
            return new PageCommitAnswer(operation.Key, PageCommitOutcome.TargetExists);
        }

        string prefix = operation.From.Key + "/";
        IReadOnlyList<string> below = [.. pages.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))];
        if (below.Count > 0) {
            return new PageCommitAnswer(operation.Key, PageCommitOutcome.FolderNotEmpty, Below: below);
        }

        return string.Equals(source.Hash, operation.ExpectedHash, StringComparison.Ordinal)
            ? new PageCommitAnswer(operation.Key, PageCommitOutcome.Moved, source.Hash)
            : new PageCommitAnswer(operation.Key, PageCommitOutcome.Stale);
    }

    private async Task<PageCommitAnswer> DeleteAsync(PageCommit operation, CancellationToken cancellationToken) {
        bool deleted = await pages.DeletePageAsync(operation.Key, cancellationToken);

        return new PageCommitAnswer(operation.Key, deleted ? PageCommitOutcome.Deleted : PageCommitOutcome.SourceNotFound);
    }

    private async Task<PageCommitAnswer> MoveAsync(PageCommit operation, CancellationToken cancellationToken) {
        PageMoveStoreResult moved = await pages.MovePageAsync(
            operation.From!, operation.Target!, operation.ExpectedHash!, cancellationToken);

        return new PageCommitAnswer(operation.Key, moved.Outcome switch {
            PageMoveStoreOutcome.Moved => PageCommitOutcome.Moved,
            PageMoveStoreOutcome.SourceNotFound => PageCommitOutcome.SourceNotFound,
            PageMoveStoreOutcome.TargetExists => PageCommitOutcome.TargetExists,
            PageMoveStoreOutcome.FolderNotEmpty => PageCommitOutcome.FolderNotEmpty,
            _ => PageCommitOutcome.Stale,
        }, moved.Hash, moved.Below);
    }

    private async Task<PageCommitAnswer> PutAsync(PageCommit operation, CancellationToken cancellationToken) {
        if (Failure is { } failure) {
            throw failure;
        }

        SaveCount++;
        LastCancellationToken = cancellationToken;
        Saved.Add(operation.Revision!);

        await pages.SavePageAsync(operation.Revision!, cancellationToken);

        return new PageCommitAnswer(operation.Key, PageCommitOutcome.Saved, operation.Revision!.Hash);
    }

}
