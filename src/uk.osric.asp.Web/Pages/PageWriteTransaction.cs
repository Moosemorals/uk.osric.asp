// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Runtime.ExceptionServices;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;
using PageDiagnostic = uk.osric.asp.Sdk.PageDiagnostic;
using PageKind = uk.osric.asp.Core.Pages.PageKind;

/// <summary>One render's transaction: the writes staged in it, and its one commit.</summary>
/// <remarks>
/// <para>
/// A page opens it with <see cref="IPageWrites.BeginTransaction"/> and writes with the members it
/// already knows — a write made while this is open joins it here rather than keeping a second spelling
/// of every member. Staging resolves the key, reads what the write needs to decide and compiles a save,
/// and never writes: the store is touched at <see cref="CommitAsync"/>, which is what lets a page hold
/// several writes without holding the store's writer for the length of a compile slot.
/// </para>
/// <para>
/// The commit applies the operations in the order they were staged, each proved against what the ones
/// before it wrote: create a page, move it and delete it, and all three succeed. A refusal is the
/// report's answer, not an exception: nothing is stored, and the rows say what each operation became.
/// Page bugs — a reserved key, a key that is not addressable, a library moved — throw while staging,
/// before anything is at risk.
/// </para>
/// <para>
/// A staged save reads the libraries staged before it as well as the store's, in the order the page
/// made the calls. A library staged after a page that uses it is not corrected: the page was built
/// from the library that was there and its closure makes it stale, exactly as a dependent outside the
/// transaction is.
/// </para>
/// </remarks>
internal sealed class PageWriteTransaction(
    PageEditor pages,
    PageSeeds seeds,
    PageTransactionGate gate,
    PageWriteTransaction? root,
    CancellationToken deadline) : IPageTransaction {

    private readonly List<PageWriteResult> _rows = [];
    private readonly List<PageCommit> _operations = [];
    private readonly List<int> _rowOfOperation = [];
    private readonly Dictionary<string, PageLibrary> _stagedLibraries = [with(StringComparer.Ordinal)];
    private Exception? _fault;
    private bool _committed;

    /// <summary>The transaction this one nests inside, or <see langword="null"/> when it is the outermost.</summary>
    internal PageWriteTransaction? Parent => root;

    /// <summary>Stages this page's own source, bound to the layer's key.</summary>
    /// <param name="path">The key of the page that is saving itself.</param>
    /// <param name="template">The template text.</param>
    public Task<PageWriteResult> SaveOwnAsync(PagePath path, string template) =>
        StageSaveAsync(path, template, null, null, PageWriteOperation.Save, PageCommitKind.Put);

    /// <summary>Stages a page, only while its head still has the expected revision.</summary>
    /// <param name="target">The key to save.</param>
    /// <param name="template">The template text.</param>
    /// <param name="expectedHash">The head hash the form was rendered from, or <see langword="null"/> for a create.</param>
    public Task<PageWriteResult> SaveAsync(PagePath target, string template, string? expectedHash) =>
        StageSaveAsync(target, template, expectedHash, null, PageWriteOperation.Edit);

    /// <summary>Stages a move of a page, its history and its state.</summary>
    /// <param name="source">The key the page has.</param>
    /// <param name="target">The key it should have.</param>
    /// <param name="expectedHash">The head the caller read.</param>
    public Task<PageWriteResult> MoveAsync(PagePath source, PagePath target, string expectedHash) {
        ThrowIfCommitted();

        if (string.Equals(source.Key, target.Key, StringComparison.Ordinal)) {
            return Task.FromResult(Remember(target, PageWriteOperation.Move, PageWriteOutcome.Unchanged, null, []));
        }

        return Task.FromResult(Stage(new PageCommit(PageCommitKind.Move, target, expectedHash, From: source, Target: target),
            target, PageWriteOperation.Move, null, []));
    }

    /// <summary>Stages a tombstone for a page, keeping its history.</summary>
    /// <param name="target">The key to tombstone.</param>
    public Task<PageWriteResult> DeleteAsync(PagePath target) {
        ThrowIfCommitted();

        return Task.FromResult(Stage(new PageCommit(PageCommitKind.Delete, target), target, PageWriteOperation.Delete, null, []));
    }

    /// <summary>Stages a retained revision's source as a new revision.</summary>
    /// <param name="target">The page's key.</param>
    /// <param name="hash">The retained revision's hash.</param>
    /// <param name="expectedHash">The live head the form was rendered from, or null for a tombstone.</param>
    public async Task<PageWriteResult> RestoreAsync(PagePath target, string hash, string? expectedHash) {
        ThrowIfCommitted();

        PageRevisionTemplate? revision = await pages.GetRevisionAsync(target, hash, deadline);
        if (revision is null) {
            return Remember(target, PageWriteOperation.Restore, PageWriteOutcome.Missing, null, []);
        }

        PageTemplate? live = await pages.GetAsync(target, deadline);
        if (PageSourceChange.IsNoOp(live?.Template, revision.Template)) {
            return Remember(target, PageWriteOperation.Restore, PageWriteOutcome.Unchanged, live?.Hash, []);
        }

        return await StageSaveAsync(target, revision.Template, live is null ? null : expectedHash, null, PageWriteOperation.Restore);
    }

    /// <summary>Stages recording a seed as installed without storing a revision.</summary>
    /// <param name="target">The page's key.</param>
    public async Task<PageWriteResult> AdoptAsync(PagePath target) {
        ThrowIfCommitted();

        SeedFile? seed = seeds.Find(target);
        if (seed is null) {
            return Remember(target, PageWriteOperation.Adopt, PageWriteOutcome.Missing, null, []);
        }

        PageHeadSummary? head = await pages.GetHeadAsync(target, deadline);
        SeedComparison comparison = SeedComparison.Of(head?.SeedHash, seed.Hash, head?.ContentHash);

        if (head is null || head.IsDeleted) {
            throw new InvalidOperationException(
                $"{target.Key} is {Words(comparison, seed.Hash)}; only a live page that already holds the seed can be recorded as installed.");
        }

        if (string.Equals(head.SeedHash, seed.Hash, StringComparison.Ordinal)) {
            PageTemplate? already = await pages.GetAsync(target, deadline);
            return Remember(target, PageWriteOperation.Adopt, PageWriteOutcome.Unchanged, already?.Hash, []);
        }

        if (!comparison.CanAdopt) {
            throw new InvalidOperationException(
                $"{target.Key} is {Words(comparison, seed.Hash)}; only a page that already holds the shipped seed can be recorded as installed.");
        }

        return Stage(new PageCommit(PageCommitKind.Adopt, target, SeedHash: seed.Hash),
            target, PageWriteOperation.Adopt, null, []);
    }

    /// <summary>Stages a page the release ships with from its bundled source.</summary>
    /// <param name="target">The page's key.</param>
    /// <param name="expectedHash">The live head the form was rendered from, or null for a tombstone.</param>
    public async Task<PageWriteResult> InstallAsync(PagePath target, string? expectedHash) {
        ThrowIfCommitted();

        SeedFile? seed = seeds.Find(target);
        if (seed is null) {
            return Remember(target, PageWriteOperation.Install, PageWriteOutcome.Missing, null, []);
        }

        PageHeadSummary? head = await pages.GetHeadAsync(target, deadline);
        if (head is not null) {
            SeedComparison comparison = SeedComparison.Of(head.SeedHash, seed.Hash, head.ContentHash);
            if (comparison.Verdict == SeedVerdict.InSync) {
                PageTemplate? already = await pages.GetAsync(target, deadline);
                return Remember(target, PageWriteOperation.Install, PageWriteOutcome.Unchanged, already?.Hash, []);
            }

            if (!comparison.CanApply) {
                throw new InvalidOperationException(
                    $"{target.Key} is {Words(comparison, seed.Hash)}; the release does not overwrite it automatically.");
            }
        }

        return await StageSaveAsync(target, seed.Source, head is null ? null : expectedHash, seed.Hash, PageWriteOperation.Install);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageWriteResult>> CommitAsync() {
        ThrowIfCommitted();

        _committed = true;

        if (root is not null) {
            // A nested transaction does not touch the store: its writes become the outer one's, so
            // they land when it commits and vanish if it is disposed. Its own rows stay Staged,
            // because nothing is stored until the outermost commit says so.
            root.Absorb(this);
            return [.. _rows];
        }

        gate.Close(this);

        if (_operations.Count == 0) {
            return [.. _rows];
        }

        // A write that has begun is never abandoned: the render's deadline bounds a compile slot,
        // not the author's edit (`pages-a-page-may-save-itself`).
        IReadOnlyList<PageCommitAnswer> answers = await pages.CommitAsync(_operations, CancellationToken.None);

        for (int index = 0; index < answers.Count && index < _rowOfOperation.Count; index++) {
            int row = _rowOfOperation[index];
            _rows[row] = _rows[row] with {
                Outcome = Outcome(answers[index].Outcome),
                Hash = answers[index].Hash ?? _rows[row].Hash,
                Below = answers[index].Below,
            };
        }

        if (_fault is { } fault) {
            ExceptionDispatchInfo.Capture(fault).Throw();
        }

        return [.. _rows];
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PageWriteResult>> RollbackAsync() {
        ThrowIfCommitted();

        _committed = true;
        if (root is null) {
            gate.Close(this);
        }

        foreach (int row in _rowOfOperation) {
            _rows[row] = _rows[row] with { Outcome = PageWriteOutcome.NotAttempted };
        }

        return Task.FromResult<IReadOnlyList<PageWriteResult>>([.. _rows]);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() {
        if (!_committed) {
            _committed = true;
            if (root is null) {
                gate.Close(this);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Takes a nested transaction's writes and rows into this one, in order.</summary>
    /// <param name="child">The transaction that has just committed.</param>
    /// <remarks>
    /// The operations simply join the end of this one, because a commit applies them in order and each
    /// proves itself against what the ones before it wrote. Nothing is refused for naming a key this
    /// transaction already has: a create, a move and a delete of the same page in one unit is a
    /// sequence, not a conflict.
    /// </remarks>
    internal void Absorb(PageWriteTransaction child) {
        ArgumentNullException.ThrowIfNull(child);

        int offset = _rows.Count;
        _rows.AddRange(child._rows);

        for (int index = 0; index < child._operations.Count; index++) {
            _operations.Add(child._operations[index]);
            _rowOfOperation.Add(offset + child._rowOfOperation[index]);
        }

        _fault ??= child._fault;
    }

    private static PageWriteOutcome Outcome(PageCommitOutcome outcome) => outcome switch {
        PageCommitOutcome.Saved => PageWriteOutcome.Saved,
        PageCommitOutcome.Unchanged => PageWriteOutcome.Unchanged,
        PageCommitOutcome.Stale => PageWriteOutcome.Stale,
        PageCommitOutcome.Moved => PageWriteOutcome.Moved,
        PageCommitOutcome.Deleted => PageWriteOutcome.Deleted,
        PageCommitOutcome.SourceNotFound => PageWriteOutcome.SourceNotFound,
        PageCommitOutcome.TargetExists => PageWriteOutcome.TargetExists,
        PageCommitOutcome.FolderNotEmpty => PageWriteOutcome.FolderNotEmpty,
        PageCommitOutcome.Refused => PageWriteOutcome.Refused,
        PageCommitOutcome.Busy => PageWriteOutcome.Busy,
        PageCommitOutcome.RolledBack => PageWriteOutcome.RolledBack,
        _ => PageWriteOutcome.NotAttempted,
    };

    private static string Words(SeedComparison comparison, string shipped) => comparison.Describe(shipped).Verdict;

    /// <summary>Stages a save, compiling it against the libraries staged before it.</summary>
    private async Task<PageWriteResult> StageSaveAsync(
        PagePath target,
        string template,
        string? expectedHash,
        string? seedHash,
        PageWriteOperation operation,
        PageCommitKind? unconditional = null) {
        ThrowIfCommitted();

        // A staged save sees the libraries staged before it as well as the store's, in the order the
        // page made the calls: what it names is what it saw, and its closure says so.
        IReadOnlyList<PageLibrary> staged = [.. _stagedLibraries.Values];
        StagedRevision built = await pages.BuildAsync(
            target, template, seedHash, staged, CancellationToken.None, deadline);

        bool creates = string.IsNullOrWhiteSpace(expectedHash);
        PageCommit commit = new(
            unconditional ?? (creates ? PageCommitKind.Create : PageCommitKind.Save),
            target,
            creates ? null : expectedHash,
            built.Revision,
            SeedHash: seedHash);

        PageWriteResult row = Stage(commit, target, operation, built.Revision.Hash, PageDiagnostics.ToSdk(built.Diagnostics));
        if (built.Revision.Image?.Kind == PageKind.Library) {
            _stagedLibraries[target.Key] = new PageLibrary(target, built.Revision.Hash, template);
        }

        // The source is stored with the rest of the transaction and the compiler's fault is rethrown
        // by the commit, so the author's edit is never lost to our bug.
        _fault ??= built.Fault;

        return row;
    }

    private PageWriteResult Stage(PageCommit operation, PagePath key, PageWriteOperation kind, string? hash, IReadOnlyList<PageDiagnostic> diagnostics) {
        _operations.Add(operation);
        _rowOfOperation.Add(_rows.Count);

        return Remember(key, kind, PageWriteOutcome.Staged, hash, diagnostics);
    }

    private PageWriteResult Remember(PagePath key, PageWriteOperation kind, PageWriteOutcome outcome, string? hash, IReadOnlyList<PageDiagnostic> diagnostics) {
        PageWriteResult row = new(key.Key, kind, outcome, hash, diagnostics);
        _rows.Add(row);

        return row;
    }

    private void ThrowIfCommitted() {
        if (_committed) {
            throw new InvalidOperationException("This transaction has been committed or disposed; nothing more can be staged on it.");
        }
    }
}
