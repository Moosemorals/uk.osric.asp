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

/// <summary>The capability's transaction: writes staged against the render, committed once.</summary>
/// <remarks>
/// <para>
/// Staging resolves keys, reads what a write needs to decide and compiles a save, and never writes:
/// the store is touched at <see cref="CommitAsync"/>, where every condition is proved again before
/// anything is stored. That is what lets a page hold several writes without holding the store's
/// writer, which a compile inside a transaction would keep for the length of a compile slot.
/// </para>
/// <para>
/// A refusal is the report's answer, not an exception: nothing is written and the rows say what each
/// operation saw. Page bugs — a reserved key, a key that is not addressable, a second operation on a
/// key already staged, a library moved — throw while staging, before anything is at risk.
/// </para>
/// <para>
/// A staged save reads the libraries staged before it as well as the store's, in the order the page
/// made the calls. A library staged after a page that uses it is not corrected: the page was built
/// from the library that was there and its closure makes it stale, exactly as a dependent outside the
/// transaction is.
/// </para>
/// </remarks>
internal sealed class PageTransactionScope : RenderBoundCapability, IPageTransaction {

    private readonly PagePath _path;
    private readonly PageEditor _pages;
    private readonly ReservedPaths _reserved;
    private readonly PageSeeds _seeds;
    private readonly PageTransactionGate _gate;
    private readonly List<PageWriteResult> _rows = [];
    private readonly List<PageCommit> _operations = [];
    private readonly List<int> _rowOfOperation = [];
    private readonly HashSet<string> _keys = [with(StringComparer.Ordinal)];
    private readonly Dictionary<string, PageLibrary> _stagedLibraries = [with(StringComparer.Ordinal)];
    private Exception? _fault;
    private bool _committed;

    /// <summary>Opens a transaction for one render.</summary>
    /// <param name="path">The page whose render opened it.</param>
    /// <param name="pages">The store's write capability, for compiling and committing.</param>
    /// <param name="reserved">The shell's route table, which no page may write.</param>
    /// <param name="seeds">The pages this release ships, for an install or an adopt.</param>
    /// <param name="gate">The render's one-transaction gate, which the scope already holds.</param>
    /// <param name="deadline">The render's deadline, which bounds a compile slot and nothing else.</param>
    public PageTransactionScope(
        PagePath path,
        PageEditor pages,
        ReservedPaths reserved,
        PageSeeds seeds,
        PageTransactionGate gate,
        CancellationToken deadline) {
        _path = path;
        _pages = pages;
        _reserved = reserved;
        _seeds = seeds;
        _gate = gate;
        AttachDeadline(deadline);
    }

    /// <inheritdoc/>
    public async Task<PageWriteResult> SaveAsync(string template) {
        ArgumentNullException.ThrowIfNull(template);
        ThrowIfCommitted();

        return await StageSaveAsync(_path, template, null, null, PageWriteOperation.Save, PageCommitKind.Put);
    }

    /// <inheritdoc/>
    public async Task<PageWriteResult> SaveAsync(string key, string template, string? expectedHash) {
        ArgumentNullException.ThrowIfNull(template);
        ThrowIfCommitted();

        PagePath target = ResolveForWrite(key);

        return await StageSaveAsync(target, template, expectedHash, null, PageWriteOperation.Edit);
    }

    /// <inheritdoc/>
    public Task<PageWriteResult> MoveAsync(string from, string to, string expectedHash) {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);
        ThrowIfCommitted();

        PagePath source = ResolveForWrite(from);
        PagePath target = ResolveForWrite(to);

        if (string.Equals(source.Key, _path.Key, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"{_path.Key} cannot move itself: the rest of this render answers for that key, and the response in hand is the page as it was.");
        }

        if (string.Equals(source.Key, target.Key, StringComparison.Ordinal)) {
            ReserveKeys(source.Key);
            return Task.FromResult(Remember(target, PageWriteOperation.Move, PageWriteOutcome.Unchanged, null, []));
        }

        ReserveKeys(source.Key, target.Key);

        return Task.FromResult(Stage(new PageCommit(PageCommitKind.Move, target, expectedHash, From: source, Target: target),
            target, PageWriteOperation.Move, null, []));
    }

    /// <inheritdoc/>
    public Task<PageWriteResult> DeleteAsync(string key) {
        ThrowIfCommitted();

        PagePath target = ResolveForWrite(key);
        ReserveKeys(target.Key);

        return Task.FromResult(Stage(new PageCommit(PageCommitKind.Delete, target), target, PageWriteOperation.Delete, null, []));
    }

    /// <inheritdoc/>
    public async Task<PageWriteResult> RestoreRevisionAsync(string key, string hash, string? expectedHash) {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ThrowIfCommitted();

        PagePath target = ResolveForWrite(key);
        PageRevisionTemplate? revision = await _pages.GetRevisionAsync(target, hash, Deadline);
        if (revision is null) {
            return Remember(target, PageWriteOperation.Restore, PageWriteOutcome.Missing, null, []);
        }

        PageTemplate? live = await _pages.GetAsync(target, Deadline);
        if (PageSourceChange.IsNoOp(live?.Template, revision.Template)) {
            return Remember(target, PageWriteOperation.Restore, PageWriteOutcome.Unchanged, live?.Hash, []);
        }

        return await StageSaveAsync(target, revision.Template, live is null ? null : expectedHash, null, PageWriteOperation.Restore);
    }

    /// <inheritdoc/>
    public async Task<PageWriteResult> AdoptSeedAsync(string key) {
        ThrowIfCommitted();

        PagePath target = ResolveForWrite(key);
        SeedFile? seed = _seeds.Find(target);
        if (seed is null) {
            return Remember(target, PageWriteOperation.Adopt, PageWriteOutcome.Missing, null, []);
        }

        PageHeadSummary? head = await _pages.GetHeadAsync(target, Deadline);
        SeedComparison comparison = SeedComparison.Of(head?.SeedHash, seed.Hash, head?.ContentHash);

        if (head is null || head.IsDeleted) {
            throw new InvalidOperationException(
                $"{target.Key} is {Words(comparison, seed.Hash)}; only a live page that already holds the seed can be recorded as installed.");
        }

        if (string.Equals(head.SeedHash, seed.Hash, StringComparison.Ordinal)) {
            PageTemplate? already = await _pages.GetAsync(target, Deadline);
            return Remember(target, PageWriteOperation.Adopt, PageWriteOutcome.Unchanged, already?.Hash, []);
        }

        if (!comparison.CanAdopt) {
            throw new InvalidOperationException(
                $"{target.Key} is {Words(comparison, seed.Hash)}; only a page that already holds the shipped seed can be recorded as installed.");
        }

        ReserveKeys(target.Key);

        return Stage(new PageCommit(PageCommitKind.Adopt, target, SeedHash: seed.Hash),
            target, PageWriteOperation.Adopt, null, []);
    }

    /// <inheritdoc/>
    public async Task<PageWriteResult> InstallSeedAsync(string key, string? expectedHash) {
        ThrowIfCommitted();

        PagePath target = ResolveForWrite(key);
        SeedFile? seed = _seeds.Find(target);
        if (seed is null) {
            return Remember(target, PageWriteOperation.Install, PageWriteOutcome.Missing, null, []);
        }

        PageHeadSummary? head = await _pages.GetHeadAsync(target, Deadline);
        if (head is not null) {
            SeedComparison comparison = SeedComparison.Of(head.SeedHash, seed.Hash, head.ContentHash);
            if (comparison.Verdict == SeedVerdict.InSync) {
                PageTemplate? already = await _pages.GetAsync(target, Deadline);
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
        _gate.Close();

        if (_operations.Count == 0) {
            return [.. _rows];
        }

        // A write that has begun is never abandoned: the render's deadline bounds a compile slot,
        // not the author's edit (`pages-a-page-may-save-itself`).
        IReadOnlyList<PageCommitAnswer> answers = await _pages.CommitAsync(_operations, CancellationToken.None);

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
    public ValueTask DisposeAsync() {
        if (!_committed) {
            _committed = true;
            _gate.Close();
        }

        return ValueTask.CompletedTask;
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
        ReserveKeys(target.Key);

        // A staged save sees the libraries staged before it as well as the store's, in the order the
        // page made the calls: what it names is what it saw, and its closure says so.
        IReadOnlyList<PageLibrary> staged = [.. _stagedLibraries.Values];
        StagedRevision built = await _pages.BuildAsync(
            target, template, seedHash, staged, CancellationToken.None, Deadline);

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

    private void ReserveKeys(string key, string? other = null) {
        if (!_keys.Add(key) || (other is not null && !_keys.Add(other))) {
            throw new InvalidOperationException(
                $"{key} is staged twice in one transaction: two operations on one key cannot both hold their head, so one of them would be lost.");
        }
    }

    private void ThrowIfCommitted() {
        if (_committed) {
            throw new InvalidOperationException("This transaction has been committed or disposed; nothing more can be staged on it.");
        }
    }

    private PagePath ResolveForWrite(string key) {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {_path.Key} was abandoned; it cannot edit a page.");
        }

        if (Deadline.IsCancellationRequested) {
            throw new InvalidOperationException($"{_path.Key} has run out of time; it cannot edit a page.");
        }

        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("A page key is required.", nameof(key));
        }

        PagePath target = PagePath.Normalize(key);
        if (!target.IsAddressable) {
            throw new ArgumentException("The page key is not addressable.", nameof(key));
        }

        if (_reserved.IsReserved(target)) {
            throw new InvalidOperationException($"{target.Key} is a shell file; no page is stored there.");
        }

        return target;
    }
}
