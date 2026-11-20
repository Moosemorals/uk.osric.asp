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
/// render, a second write and a reserved key are page bugs and throw
/// <see cref="InvalidOperationException"/>. The key itself is the page's answer through
/// <see cref="Resolve"/>, so a typo in a query value is a message the page renders.
/// </para>
/// <para>
/// Every member here is a transaction of one, and <see cref="BeginTransaction"/> is several writes that
/// become the store's state together. A save is conditional on the head hash the form carried when it
/// names a key, and a write that has begun is not abandoned: the render's deadline bounds the wait for
/// a compile slot, not the author's edit (`pages-a-page-may-save-itself`).
/// </para>
/// </remarks>
internal sealed class PageWritesScope(
    PagePath path,
    PageEditor pages,
    PageUser? user,
    bool writable,
    ReservedPaths reserved,
    PageSeeds seeds,
    PageTransactionGate gate) : RenderBoundCapability, IPageWrites {

    /// <inheritdoc/>
    public IPageTransaction BeginTransaction() {
        ThrowIfCannotWrite();

        // A transaction opened while one is already open nests inside it: its writes become the outer
        // one's and land when it commits. That is what an embedded
        // page's transaction is, and it is why nothing here is refused.
        PageTransactionScope? root = gate.Root;

        PageTransactionScope transaction = new(path, pages, reserved, seeds, gate, Deadline, root);
        transaction.AttachDeadline(Deadline);
        if (root is null) {
            gate.Open(transaction);
        }

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

        PageWriteResult row = await OneAsync(transaction => transaction.SaveAsync(template));

        return new PageSaveResult(row.Hash ?? string.Empty, row.Diagnostics);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult> SaveAsync(string key, string template, string? expectedHash) {
        ArgumentNullException.ThrowIfNull(template);

        PageWriteResult row = await OneAsync(transaction => transaction.SaveAsync(key, template, expectedHash));

        return new PageEditResult(EditOutcome(row.Outcome), row.Hash, row.Diagnostics);
    }

    /// <inheritdoc/>
    public async Task<PageDeleteResult> DeleteAsync(string key) {
        ThrowIfCannotWrite();

        PagePath target = ResolveForWrite(key);
        PageHeadSummary? head = await pages.GetHeadAsync(target, Deadline);
        bool wasLive = head is { IsDeleted: false };

        PageWriteResult row = await OneAsync(transaction => transaction.DeleteAsync(key));

        return new PageDeleteResult(row.Outcome == PageWriteOutcome.Deleted, wasLive);
    }

    /// <inheritdoc/>
    public async Task<PageMoveResult> MoveAsync(string from, string to, string expectedHash) {
        PageWriteResult row = await OneAsync(transaction => transaction.MoveAsync(from, to, expectedHash));

        return new PageMoveResult(MoveOutcome(row.Outcome), row.Hash, row.Below ?? []);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> RestoreRevisionAsync(string key, string hash, string? expectedHash) {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        PageWriteResult row = await OneAsync(transaction => transaction.RestoreRevisionAsync(key, hash, expectedHash));

        return row.Outcome == PageWriteOutcome.Missing
            ? null
            : new PageEditResult(EditOutcome(row.Outcome), row.Hash, row.Diagnostics);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> AdoptSeedAsync(string key) {
        PageWriteResult row = await OneAsync(transaction => transaction.AdoptSeedAsync(key));

        return row.Outcome == PageWriteOutcome.Missing
            ? null
            : new PageEditResult(EditOutcome(row.Outcome), row.Hash, row.Diagnostics);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> InstallSeedAsync(string key, string? expectedHash) {
        PageWriteResult row = await OneAsync(transaction => transaction.InstallSeedAsync(key, expectedHash));

        return row.Outcome == PageWriteOutcome.Missing
            ? null
            : new PageEditResult(EditOutcome(row.Outcome), row.Hash, row.Diagnostics);
    }

    /// <summary>Makes one write as a transaction of one, so every write has one path.</summary>
    /// <remarks>
    /// It is that write's own transaction and not part of one this render has open, because "save
    /// this" means it is stored when the call answers. Only <see cref="BeginTransaction"/> defers a
    /// write, and a transaction opened while another is open is what nests.
    /// </remarks>
    private async Task<PageWriteResult> OneAsync(Func<IPageTransaction, Task<PageWriteResult>> stage) {
        ThrowIfCannotWrite();

        await using IPageTransaction transaction = new PageTransactionScope(path, pages, reserved, seeds, gate: null, Deadline);
        _ = await stage(transaction);
        IReadOnlyList<PageWriteResult> report = await transaction.CommitAsync();

        return report[0];
    }

    private static PageEditOutcome EditOutcome(PageWriteOutcome outcome) => outcome switch {
        PageWriteOutcome.Saved => PageEditOutcome.Saved,
        PageWriteOutcome.Unchanged => PageEditOutcome.Unchanged,
        PageWriteOutcome.Busy => PageEditOutcome.Busy,
        _ => PageEditOutcome.Stale,
    };

    private static PageMoveOutcome MoveOutcome(PageWriteOutcome outcome) => outcome switch {
        PageWriteOutcome.Moved => PageMoveOutcome.Moved,
        PageWriteOutcome.Unchanged => PageMoveOutcome.Unchanged,
        PageWriteOutcome.SourceNotFound => PageMoveOutcome.SourceNotFound,
        PageWriteOutcome.TargetExists => PageMoveOutcome.TargetExists,
        PageWriteOutcome.FolderNotEmpty => PageMoveOutcome.FolderNotEmpty,
        PageWriteOutcome.Busy => PageMoveOutcome.Busy,
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
