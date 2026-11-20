// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IPageStore"/> that records what the shell asked of it.</summary>
internal sealed class FakePageStore : IPageStore {
    private readonly Dictionary<string, PageTemplate> _pages = [];
    private readonly Dictionary<string, bool> _heads = [];
    private readonly Dictionary<string, PageTemplate> _revisions = [];
    private readonly Dictionary<string, string> _anchors = [];

    public Exception? Failure { get; init; }

    /// <summary>
    /// Lets a test simulate a save that lands while a page is compiling: the replacement is stored
    /// and the conditional write is refused. Runs once.
    /// </summary>
    public Func<PagePath, string, PageTemplate?>? ReplaceOnImageWrite { get; set; }

    /// <summary>
    /// Lets a test simulate a save that lands between a caller reading a page and its conditional
    /// write: the replacement is stored, so the write is refused. Runs once.
    /// </summary>
    public Func<PagePath, PageTemplate?>? ReplaceOnConditionalSave { get; set; }

    public bool DeleteOnImageWrite { get; set; }

    /// <summary>
    /// Fails the write of one key, so a test can prove that a caller writing many rows carries on.
    /// </summary>
    public Func<PagePath, Exception?>? FailOnWrite { get; set; }

    public int ReadCount { get; private set; }

    public int WriteCount { get; private set; }

    public int ImageWriteCount { get; private set; }

    /// <summary>Spends time in every read, so a test can exhaust a render's budget before it runs.</summary>
    public TimeSpan ReadDelay { get; set; }

    public PagePath? LastReadPath { get; private set; }

    public PagePath? LastWritePath { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public PageTemplate? this[string path] {
        get => _pages.GetValueOrDefault(PagePath.Normalize(path).Key);
        set {
            PageTemplate page = value ?? throw new ArgumentNullException(nameof(value));
            _pages[PagePath.Normalize(path).Key] = page;
            _heads[PagePath.Normalize(path).Key] = false;
            _revisions[page.Hash] = page;
            if (page.SeedHash is not null) {
                _anchors[PagePath.Normalize(path).Key] = page.SeedHash;
            }
        }
    }

    public async Task<PageTemplate?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);

        ReadCount++;
        LastReadPath = path;

        if (ReadDelay > TimeSpan.Zero) {
            await Task.Delay(ReadDelay, cancellationToken);
        }

        return _heads.GetValueOrDefault(path.Key) ? null : _pages.GetValueOrDefault(path.Key);
    }

    public Task<string?> GetSourceAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        ReadCount++;
        LastReadPath = path;
        return Task.FromResult(_heads.GetValueOrDefault(path.Key) ? null : _pages.GetValueOrDefault(path.Key)?.Template);
    }

    /// <summary>The build the listing is judged against, or null to leave staleness unjudged.</summary>
    public string? CurrentRuntimeId { get; set; }

    public Task<IReadOnlyList<PageListRow>> ListPagesAsync(CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        IReadOnlyList<PageListRow> result = [.. _heads
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PageListRow(
                PagePath.Normalize(item.Key),
                item.Value,
                StateOf(_pages.GetValueOrDefault(item.Key)?.Image)))];
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PagePath>> ListStoredPathsAsync(CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        IReadOnlyList<PagePath> result = [.. _heads.Keys
            .Union(_revisions.Values.Select(page => page.Path.Key), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(PagePath.Normalize)];
        return Task.FromResult(result);
    }

    public Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        if (!_heads.TryGetValue(path.Key, out bool isDeleted)) {
            return Task.FromResult<PageHeadSummary?>(null);
        }

        string? contentHash = _pages.TryGetValue(path.Key, out PageTemplate? page)
            ? TemplateFingerprint.Of(page.Template)
            : null;

        return Task.FromResult<PageHeadSummary?>(
            new PageHeadSummary(path, isDeleted, _anchors.GetValueOrDefault(path.Key), contentHash));
    }

    public Task<PageTemplate?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        return Task.FromResult(_revisions.GetValueOrDefault(hash));
    }

    public Task<IReadOnlyList<PageRevisionSummary>> ListRevisionsAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        string? currentRevisionId = _heads.GetValueOrDefault(path.Key) ? null : _pages.GetValueOrDefault(path.Key)?.RevisionId;
        IReadOnlyList<PageRevisionSummary> revisions = [.. _revisions.Values
            .Where(page => page.Path == path)
            .OrderByDescending(page => page.SavedAt)
            .ThenByDescending(page => page.Hash, StringComparer.Ordinal)
            .Select(page => new PageRevisionSummary(
                page.Hash,
                page.SavedAt,
                page.CreationRuntimeId,
                StateOf(page.Image),
                page.RevisionId == currentRevisionId))];
        return Task.FromResult(revisions);
    }

    public Task<PageRevisionTemplate?> GetRevisionTemplateByHashAsync(string hash, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        PageTemplate? page = _revisions.GetValueOrDefault(hash);
        return Task.FromResult(page is null
            ? null
            : new PageRevisionTemplate(page.Path, page.Hash, page.SavedAt, page.CreationRuntimeId,
                StateOf(page.Image),
                page.Template));
    }

    public Task SavePageAsync(PageTemplate page, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);
        Check(cancellationToken);

        if (FailOnWrite?.Invoke(page.Path) is { } failure) {
            throw failure;
        }

        WriteCount++;
        LastWritePath = page.Path;
        _pages[page.Path.Key] = page;
        _heads[page.Path.Key] = false;
        _revisions[page.Hash] = page;
        if (page.SeedHash is not null) {
            _anchors[page.Path.Key] = page.SeedHash;
        }

        return Task.CompletedTask;
    }

    public Task<bool> TrySavePageAsync(PageTemplate page, string expectedHash, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);
        Check(cancellationToken);

        if (ReplaceOnConditionalSave?.Invoke(page.Path) is { } raced) {
            ReplaceOnConditionalSave = null;
            _pages[page.Path.Key] = raced;
            _heads[page.Path.Key] = false;
            _revisions[raced.Hash] = raced;
            if (raced.SeedHash is not null) {
                _anchors[page.Path.Key] = raced.SeedHash;
            }
        }

        if (_heads.GetValueOrDefault(page.Path.Key)
            || !string.Equals(_pages.GetValueOrDefault(page.Path.Key)?.Hash, expectedHash, StringComparison.Ordinal)) {
            return Task.FromResult(false);
        }

        return SaveAndSucceedAsync(page, cancellationToken);
    }

    private async Task<bool> SaveAndSucceedAsync(PageTemplate page, CancellationToken cancellationToken) {
        await SavePageAsync(page, cancellationToken);
        return true;
    }

    public Task<bool> TrySetImageAsync(string revisionId, string expectedTemplate, PageImage image, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(image);
        Check(cancellationToken);

        ImageWriteCount++;
        PageTemplate? target = _revisions.Values.SingleOrDefault(page => page.RevisionId == revisionId);
        if (target is null) {
            return Task.FromResult(false);
        }

        PagePath path = target.Path;
        LastWritePath = path;

        if (ReplaceOnImageWrite?.Invoke(path, expectedTemplate) is { } replacement) {
            ReplaceOnImageWrite = null;
            _pages[path.Key] = replacement;
            _heads[path.Key] = false;
            _revisions[replacement.Hash] = replacement;
            if (replacement.SeedHash is not null) {
                _anchors[path.Key] = replacement.SeedHash;
            }

            return Task.FromResult(false);
        }

        if (DeleteOnImageWrite) {
            DeleteOnImageWrite = false;
            _heads[path.Key] = true;
        }

        if (!string.Equals(target.Template, expectedTemplate, StringComparison.Ordinal)) {
            return Task.FromResult(false);
        }

        PageTemplate compiled = target with { Image = image, Kind = image.Kind };
        _revisions[compiled.Hash] = compiled;
        if (_pages.GetValueOrDefault(path.Key)?.RevisionId == revisionId) {
            _pages[path.Key] = compiled;
        }
        return Task.FromResult(true);
    }

    public Task<bool> AdoptSeedAsync(PagePath path, string seedHash, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedHash);
        Check(cancellationToken);

        if (_heads.GetValueOrDefault(path.Key)
            || !_pages.TryGetValue(path.Key, out PageTemplate? page)
            || !string.Equals(TemplateFingerprint.Of(page.Template), seedHash, StringComparison.Ordinal)) {
            return Task.FromResult(false);
        }

        _anchors[path.Key] = seedHash;
        return Task.FromResult(true);
    }

    public Task<PageMoveStoreResult> MovePageAsync(
        PagePath from,
        PagePath to,
        string expectedHash,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);
        Check(cancellationToken);

        if (!_pages.TryGetValue(from.Key, out PageTemplate? page)
            || _heads.GetValueOrDefault(from.Key)) {
            return Task.FromResult(PageMoveStoreResult.Of(PageMoveStoreOutcome.SourceNotFound));
        }

        if (page.Kind == PageKind.Library) {
            return Task.FromResult(PageMoveStoreResult.Of(PageMoveStoreOutcome.IsLibrary));
        }

        if (!string.Equals(page.Hash, expectedHash, StringComparison.Ordinal)) {
            return Task.FromResult(PageMoveStoreResult.Of(PageMoveStoreOutcome.Stale));
        }

        if (_heads.TryGetValue(to.Key, out bool targetDeleted) && !targetDeleted) {
            return Task.FromResult(PageMoveStoreResult.Of(PageMoveStoreOutcome.TargetExists));
        }

        IReadOnlyList<string> below = [.. _heads.Keys
            .Select(PagePath.Normalize)
            .Where(key => key.IsBelow(from))
            .Select(key => key.Key)
            .OrderBy(key => key, StringComparer.Ordinal)];
        if (below.Count > 0) {
            return Task.FromResult(new PageMoveStoreResult(PageMoveStoreOutcome.FolderNotEmpty, Below: below));
        }

        string? anchor = _anchors.GetValueOrDefault(from.Key);
        _pages.Remove(from.Key);
        _heads.Remove(from.Key);
        _anchors.Remove(from.Key);

        if (_heads.ContainsKey(to.Key)) {
            _pages.Remove(to.Key);
            _anchors.Remove(to.Key);
            _heads.Remove(to.Key);
        }

        foreach (PageTemplate revision in _revisions.Values.Where(row => row.Path == from).ToArray()) {
            _revisions[revision.Hash] = revision with { Path = to };
        }

        PageTemplate moved = page with { Path = to };
        _pages[to.Key] = moved;
        _heads[to.Key] = false;
        if (anchor is not null) {
            _anchors[to.Key] = anchor;
        }

        WriteCount++;
        LastWritePath = to;

        return Task.FromResult(new PageMoveStoreResult(PageMoveStoreOutcome.Moved, page.Hash));
    }

    public Task<bool> DeletePageAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        if (!_heads.TryGetValue(path.Key, out bool isDeleted) || isDeleted) {
            return Task.FromResult(false);
        }

        _heads[path.Key] = true;
        return Task.FromResult(true);
    }

    public async Task<bool> ScrubPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        if (!_heads.TryGetValue(path.Key, out bool isDeleted) || !isDeleted) {
            return false;
        }

        PageScrubResult result = await ScrubStoredPathAsync(path, cancellationToken);
        return result.Head;
    }

    public Task<PageScrubResult> ScrubStoredPathAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);

        bool head = _heads.Remove(path.Key);
        _pages.Remove(path.Key);
        _anchors.Remove(path.Key);
        string[] hashes = [.. _revisions
            .Where(item => item.Value.Path == path)
            .Select(item => item.Key)];
        foreach (string hash in hashes) {
            _revisions.Remove(hash);
        }

        return Task.FromResult(new PageScrubResult(head, hashes.Length, 0));
    }

    private PageImageState StateOf(PageImage? image) => PageImageStatus.Describe(
        image?.RuntimeId, image?.Bytes is not null, image?.Diagnostics.Count > 0, CurrentRuntimeId, image?.Kind);

    private void Check(CancellationToken cancellationToken) {
        LastCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        if (Failure is not null) {
            throw Failure;
        }
    }
}
