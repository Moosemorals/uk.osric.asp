// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;

/// <summary>The store's write capability, bound to one render.</summary>
/// <remarks>
/// <para>
/// The host offers this to every content layer and never to a layout. Which page asked is not part of
/// the check: the authority is the signed-in visitor, and the reserved keys are the only pages the
/// shell keeps out of reach.
/// </para>
/// <para>
/// Authority is asymmetric with the catalog. An anonymous render is refused with a
/// <see cref="UserFacingException"/> before anything is compiled or stored; a <c>HEAD</c>, an ended
/// render and a reserved key are page bugs and throw <see cref="InvalidOperationException"/>. The key
/// itself is the page's answer through <see cref="Resolve"/>, so a typo in a query value is a message
/// the page renders.
/// </para>
/// <para>
/// Every member here is a transaction of one, and <see cref="BeginTransaction"/> is several writes that
/// become the store's state together — a write made while that transaction is open joins it, so the
/// members are not repeated on <see cref="IPageTransaction"/>. A save is conditional on the head hash
/// the form carried when it names a key, and a write that has begun is not abandoned: the render's
/// deadline bounds the wait for a compile slot, not the author's edit
/// (`pages-a-page-may-save-itself`, `pages-writes-are-transactions`).
/// </para>
/// <para>
/// A render passes no credential, because the request has already been authenticated and a commit is
/// bounded by the request. A job passes the credential it borrowed from the render that started it:
/// its commits ask that credential, and one that is no longer live answers
/// <see cref="PageWriteOutcome.Revoked"/> and stores nothing.
/// </para>
/// </remarks>
internal sealed class PageWritesScope(
    PagePath path,
    PageEditor pages,
    PageUser? user,
    bool writable,
    ReservedPaths reserved,
    PageSeeds seeds,
    PageTransactionGate gate,
    IPageWriteCredential? credential = null,
    Action? touched = null) : RenderBoundCapability, IPageWrites {

    /// <inheritdoc/>
    public IPageTransaction BeginTransaction() {
        ThrowIfCannotWrite();

        return Open();
    }

    // A transaction opened while one is already open nests inside it: its writes become the outer
    // one's and land when it commits. That is what an embedded page's transaction is, and it is why
    // nothing here is refused.
    private PageWriteTransaction Open() {
        // Whatever is open now becomes the parent, and this one is what later writes join until it
        // closes — which is what lets a nested transaction own the writes made while it is open.
        PageWriteTransaction transaction = new(
            pages, seeds, gate, gate.Current, Deadline, credential, touched);
        gate.Open(transaction);

        return transaction;
    }

    /// <inheritdoc/>
    public PageKeyResolution Resolve(string key) {
        if (string.IsNullOrWhiteSpace(key)) {
            return new PageKeyResolution(null, false, "A page key is required.");
        }

        PagePath target = PagePath.Normalize(key);
        return target.IsAddressable
            ? new PageKeyResolution(target.Key, reserved.IsReserved(target), null)
            : new PageKeyResolution(null, false, "That is not a page address: a key cannot contain a query or a fragment.");
    }

    /// <inheritdoc/>
    public Task<string?> SeedSourceAsync(string key) {
        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("A page key is required.", nameof(key));
        }

        PagePath target = PagePath.Normalize(key);
        if (!target.IsAddressable) {
            throw new ArgumentException("The page key is not addressable.", nameof(key));
        }

        return Task.FromResult(seeds.Source(target));
    }

    /// <inheritdoc/>
    public async Task<PageSaveResult> SaveAsync(string template) {
        ArgumentNullException.ThrowIfNull(template);

        PageWriteResult row = await OneAsync(transaction => transaction.SaveOwnAsync(path, template));

        return new PageSaveResult(row.Hash ?? string.Empty, row.Diagnostics, EditOutcome(row.Outcome));
    }

    /// <inheritdoc/>
    public async Task<PageEditResult> SaveAsync(string key, string template, string? expectedHash) {
        ArgumentNullException.ThrowIfNull(template);

        PagePath target = ResolveForWrite(key);
        PageWriteResult row = await OneAsync(transaction => transaction.SaveAsync(target, template, expectedHash));

        return new PageEditResult(EditOutcome(row.Outcome), row.Hash, row.Diagnostics);
    }

    /// <inheritdoc/>
    public async Task<PageDeleteResult> DeleteAsync(string key) {
        PagePath target = ResolveForWrite(key);

        PageHeadSummary? head = await pages.GetHeadAsync(target, Deadline);
        bool wasLive = head is { IsDeleted: false };
        PageWriteResult row = await OneAsync(transaction => transaction.DeleteAsync(target));

        // A delete that tombstoned the page found it live, whether that was in the store or in a write
        // this transaction made earlier.
        return new PageDeleteResult(DeleteOutcome(row.Outcome), wasLive || row.Outcome == PageWriteOutcome.Deleted);
    }

    /// <inheritdoc/>
    public async Task<PageMoveResult> MoveAsync(string from, string to, string expectedHash) {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        PagePath source = ResolveForWrite(from);
        PagePath target = ResolveForWrite(to);

        if (string.Equals(source.Key, path.Key, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"{path.Key} cannot move itself: the rest of this render answers for that key, and the response in hand is the page as it was.");
        }

        PageWriteResult row = await OneAsync(transaction => transaction.MoveAsync(source, target, expectedHash));

        return new PageMoveResult(MoveOutcome(row.Outcome), row.Hash, row.Below ?? []);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> RestoreRevisionAsync(string key, string hash, string? expectedHash) {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        PagePath target = ResolveForWrite(key);
        PageWriteResult row = await OneAsync(transaction => transaction.RestoreAsync(target, hash, expectedHash));

        return row.Outcome == PageWriteOutcome.Missing
            ? null
            : new PageEditResult(EditOutcome(row.Outcome), row.Hash, row.Diagnostics);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> AdoptSeedAsync(string key) {
        PagePath target = ResolveForWrite(key);
        PageWriteResult row = await OneAsync(transaction => transaction.AdoptAsync(target));

        return row.Outcome == PageWriteOutcome.Missing
            ? null
            : new PageEditResult(EditOutcome(row.Outcome), row.Hash, row.Diagnostics);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> InstallSeedAsync(string key, string? expectedHash) {
        PagePath target = ResolveForWrite(key);
        PageWriteResult row = await OneAsync(transaction => transaction.InstallAsync(target, expectedHash));

        return row.Outcome == PageWriteOutcome.Missing
            ? null
            : new PageEditResult(EditOutcome(row.Outcome), row.Hash, row.Diagnostics);
    }

    /// <summary>Makes one write, in the transaction this render has open or in one of its own.</summary>
    /// <remarks>
    /// A lone write joins the transaction the render already has open, because an embedded page's write
    /// must be rolled back by the page's abort rather than surviving it. Its answer then says Staged
    /// rather than Saved: nothing is stored until the page commits.
    /// </remarks>
    private async Task<PageWriteResult> OneAsync(Func<PageWriteTransaction, Task<PageWriteResult>> stage) {
        ThrowIfCannotWrite();

        if (gate.Current is { } open) {
            return await stage(open);
        }

        PageWriteTransaction own = Open();
        await using (own) {
            _ = await stage(own);
            IReadOnlyList<PageWriteResult> report = await own.CommitAsync();

            return report[^1];
        }
    }

    private static PageEditOutcome EditOutcome(PageWriteOutcome outcome) => outcome switch {
        PageWriteOutcome.Saved => PageEditOutcome.Saved,
        PageWriteOutcome.Unchanged => PageEditOutcome.Unchanged,
        PageWriteOutcome.Busy => PageEditOutcome.Busy,
        PageWriteOutcome.Staged => PageEditOutcome.Staged,
        PageWriteOutcome.Revoked => PageEditOutcome.Revoked,
        _ => PageEditOutcome.Stale,
    };

    private static PageDeleteOutcome DeleteOutcome(PageWriteOutcome outcome) => outcome switch {
        PageWriteOutcome.Deleted => PageDeleteOutcome.Deleted,
        PageWriteOutcome.SourceNotFound => PageDeleteOutcome.Missing,
        PageWriteOutcome.Busy => PageDeleteOutcome.Busy,
        PageWriteOutcome.Staged => PageDeleteOutcome.Staged,
        PageWriteOutcome.Revoked => PageDeleteOutcome.Revoked,
        _ => PageDeleteOutcome.Missing,
    };

    private static PageMoveOutcome MoveOutcome(PageWriteOutcome outcome) => outcome switch {
        PageWriteOutcome.Moved => PageMoveOutcome.Moved,
        PageWriteOutcome.Unchanged => PageMoveOutcome.Unchanged,
        PageWriteOutcome.SourceNotFound => PageMoveOutcome.SourceNotFound,
        PageWriteOutcome.TargetExists => PageMoveOutcome.TargetExists,
        PageWriteOutcome.FolderNotEmpty => PageMoveOutcome.FolderNotEmpty,
        PageWriteOutcome.Busy => PageMoveOutcome.Busy,
        PageWriteOutcome.Staged => PageMoveOutcome.Staged,
        PageWriteOutcome.Revoked => PageMoveOutcome.Revoked,
        _ => PageMoveOutcome.Stale,
    };

    private PagePath ResolveForWrite(string key) {
        ThrowIfCannotWrite();

        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("A page key is required.", nameof(key));
        }

        PagePath target = PagePath.Normalize(key);
        if (!target.IsAddressable) {
            throw new ArgumentException("The page key is not addressable.", nameof(key));
        }

        if (reserved.IsReserved(target)) {
            throw new InvalidOperationException($"{target.Key} is a shell file; no page is stored there.");
        }

        return target;
    }

    // The bound save names no key, so it skips the key checks and keeps the three that every write
    // makes: a visitor state, then the two render bugs.
    private void ThrowIfCannotWrite() {
        ThrowIfAnonymous();
        ThrowIfReadOnly();
        ThrowIfEnded();
    }

    private void ThrowIfAnonymous() {
        if (user is null) {
            throw new UserFacingException("Sign in to edit a page. Nothing was saved.");
        }
    }

    private void ThrowIfReadOnly() {
        if (!writable) {
            throw new InvalidOperationException($"{path.Key} may not edit a page during a HEAD request.");
        }
    }

    private void ThrowIfEnded() {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot edit a page.");
        }

        if (Deadline.IsCancellationRequested) {
            throw new InvalidOperationException($"{path.Key} has run out of time; it cannot edit a page.");
        }
    }
}
