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
}
