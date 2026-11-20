// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The <see cref="IPageRevisionStore"/> over a context factory.</summary>
/// <remarks>
/// One context per save, created and disposed here. It calls the same
/// <see cref="SqlitePageStore.StoreRevisionAsync"/> the editor's save and an imported batch call, so
/// a page's save is one more caller of the one insert rather than a second writer (`pages-a-page-may-save-itself`).
/// </remarks>
internal sealed class SqlitePageRevisionStore(
    IPageCommitStore commits) : IPageRevisionStore {

    /// <inheritdoc/>
    public async Task<PageTemplate> SaveAsync(PageTemplate revision, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(revision);

        IReadOnlyList<PageCommitAnswer> answers = await commits.CommitAsync(
            [new PageCommit(PageCommitKind.Put, revision.Path, Revision: revision)], cancellationToken);

        return answers[0].Outcome == PageCommitOutcome.Saved
            ? answers[0].Stored ?? revision
            : throw new InvalidOperationException($"An unconditional save of {revision.Path.Key} was refused as {answers[0].Outcome}.");
    }

    /// <inheritdoc/>
    public async Task<bool> TrySaveAsync(PageTemplate revision, string expectedHash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        IReadOnlyList<PageCommitAnswer> answers = await commits.CommitAsync(
            [new PageCommit(PageCommitKind.Save, revision.Path, expectedHash, revision)], cancellationToken);

        return answers[0].Outcome == PageCommitOutcome.Saved;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PageCommitAnswer>> CommitAsync(
        IReadOnlyList<PageCommit> operations,
        CancellationToken cancellationToken = default) =>
        commits.CommitAsync(operations, cancellationToken);
}
