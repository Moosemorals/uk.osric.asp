// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;

/// <summary>What became of one page in an import.</summary>
internal enum ImportPageStatus {
    /// <summary>Not compiled yet, or not reached after the batch failed.</summary>
    Waiting,

    /// <summary>Has no code, so it is stored and served as source.</summary>
    StoredAsSource,

    /// <summary>Compiled cleanly.</summary>
    Compiled,

    /// <summary>Compiled, with warnings.</summary>
    CompiledWithWarnings,

    /// <summary>Did not compile; it is still stored, with its diagnostics.</summary>
    CompiledWithErrors,

    /// <summary>The compiler itself failed; the source is kept without an image.</summary>
    CompilerFault,
}

/// <summary>Where the batch as a whole has got to.</summary>
internal enum ImportBatchStatus {
    /// <summary>Staged and waiting to be approved.</summary>
    Reviewing,

    /// <summary>Approved and queued for the worker.</summary>
    Queued,

    /// <summary>Compiling the selected pages, with nothing written.</summary>
    Compiling,

    /// <summary>Writing every selected page in one transaction.</summary>
    Saving,

    /// <summary>Every selected page is stored.</summary>
    Committed,

    /// <summary>Nothing was written.</summary>
    Failed,
}

/// <summary>One page's place in a running import.</summary>
/// <param name="Path">The page being imported.</param>
/// <param name="Status">How far it got.</param>
/// <param name="Diagnostics">The compiler's diagnostics, when it did not compile cleanly.</param>
internal sealed record ImportPageResult(PagePath Path, ImportPageStatus Status, IReadOnlyList<PageDiagnostic> Diagnostics);

/// <summary>A consistent picture of a stage, taken under the stage's lock.</summary>
/// <param name="Status">Where the batch has got to.</param>
/// <param name="Selected">The pages the approval ticked, in key order.</param>
/// <param name="Results">One result per selected page, filled in as the worker runs.</param>
/// <param name="Refused">The pages whose merged state would not fit, when that is why the batch failed.</param>
/// <param name="Message">A sentence for the reader, when the outcome needs one.</param>
internal sealed record ImportStageSnapshot(
    ImportBatchStatus Status,
    IReadOnlyList<PagePath> Selected,
    IReadOnlyList<ImportPageResult> Results,
    IReadOnlyList<PagePath> Refused,
    string? Message);

/// <summary>One staged archive and its job state, mutated under one lock.</summary>
/// <remarks>
/// A stage is in memory only: it never touches disk, and a restart simply forgets it. Everything
/// that reads or writes the job state takes the lock, so the progress page sees a whole picture
/// rather than a half-applied one.
/// </remarks>
internal sealed class ImportStage {
    private readonly Lock _lock = new();
    private readonly Dictionary<string, PageArchiveEntry> _entries;
    private ImportPageResult[] _results = [];
    private ImportBatchStatus _status = ImportBatchStatus.Reviewing;
    private IReadOnlyList<PagePath> _selected = [];
    private IReadOnlyList<PagePath> _refused = [];
    private string? _message;

    /// <summary>Creates a stage over a validated archive.</summary>
    /// <param name="id">The stage's identifier, from the store.</param>
    /// <param name="pages">The validated pages.</param>
    /// <param name="createdAt">When the archive was staged, for the time-to-live.</param>
    public ImportStage(string id, IReadOnlyList<PageArchiveEntry> pages, DateTimeOffset createdAt) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(pages);

        Id = id;
        Pages = pages;
        CreatedAt = createdAt;
        _entries = pages.ToDictionary(page => page.Path.Key, StringComparer.Ordinal);
    }

    /// <summary>The stage's identifier.</summary>
    public string Id { get; }

    /// <summary>The validated pages, in key order.</summary>
    public IReadOnlyList<PageArchiveEntry> Pages { get; }

    /// <summary>When the archive was staged.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Whether the stage has been approved and handed to the worker.</summary>
    public bool IsRunning {
        get {
            lock (_lock) {
                return _status is ImportBatchStatus.Queued or ImportBatchStatus.Compiling or ImportBatchStatus.Saving;
            }
        }
    }

    /// <summary>Finds a page the archive carries.</summary>
    /// <param name="path">The canonical key.</param>
    public PageArchiveEntry? Find(PagePath path) =>
        _entries.GetValueOrDefault(path.Key);

    /// <summary>Marks the stage approved and queued, unless a worker already has it.</summary>
    /// <param name="selected">The pages the approval ticked, in key order.</param>
    /// <returns><see langword="false"/> when this stage is already importing.</returns>
    /// <remarks>
    /// A finished stage accepts another approval: the review offers a retry after a failure, and a
    /// second import of the same archive is an ordinary new revision rather than a special case.
    /// </remarks>
    public bool TryApprove(IReadOnlyList<PagePath> selected) {
        ArgumentNullException.ThrowIfNull(selected);

        lock (_lock) {
            if (_status is ImportBatchStatus.Queued or ImportBatchStatus.Compiling or ImportBatchStatus.Saving) {
                return false;
            }

            _selected = selected;
            _results = [.. selected.Select(path => new ImportPageResult(path, ImportPageStatus.Waiting, []))];
            _refused = [];
            _message = null;
            _status = ImportBatchStatus.Queued;
            return true;
        }
    }

    /// <summary>Puts the stage back to "not approved" after the queue refused it.</summary>
    public void Unqueue() {
        lock (_lock) {
            if (_status == ImportBatchStatus.Queued) {
                _status = ImportBatchStatus.Reviewing;
                _selected = [];
                _results = [];
            }
        }
    }

    /// <summary>Records that compilation has started.</summary>
    public void BeginCompiling() {
        lock (_lock) {
            _status = ImportBatchStatus.Compiling;
        }
    }

    /// <summary>Records that the single transaction has started.</summary>
    public void BeginSaving() {
        lock (_lock) {
            _status = ImportBatchStatus.Saving;
        }
    }

    /// <summary>Records one page's compile result.</summary>
    /// <param name="result">The result to record.</param>
    public void Record(ImportPageResult result) {
        ArgumentNullException.ThrowIfNull(result);

        lock (_lock) {
            for (int index = 0; index < _results.Length; index++) {
                if (string.Equals(_results[index].Path.Key, result.Path.Key, StringComparison.Ordinal)) {
                    _results[index] = result;
                    return;
                }
            }
        }
    }

    /// <summary>Records that every selected page was stored.</summary>
    /// <param name="message">A sentence for the reader.</param>
    public void Commit(string message) {
        lock (_lock) {
            _status = ImportBatchStatus.Committed;
            _message = message;
        }
    }

    /// <summary>Records that nothing was stored.</summary>
    /// <param name="message">A sentence naming the reason.</param>
    /// <param name="refused">The pages whose merged state would not fit, when that is the reason.</param>
    /// <remarks>A page still marked <see cref="ImportPageStatus.Waiting"/> was not compiled after the failure.</remarks>
    public void Fail(string message, IReadOnlyList<PagePath>? refused = null) {
        lock (_lock) {
            _status = ImportBatchStatus.Failed;
            _message = message;
            _refused = refused ?? [];
        }
    }

    /// <summary>Takes a consistent picture of the stage.</summary>
    public ImportStageSnapshot Snapshot() {
        lock (_lock) {
            return new ImportStageSnapshot(_status, _selected, _results, _refused, _message);
        }
    }
}
