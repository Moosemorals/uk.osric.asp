// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IPageStore"/> that records what the shell asked of it.</summary>
internal sealed class FakePageStore : IPageStore {
    private readonly Dictionary<string, StoredPage> _pages = [];
    private readonly Dictionary<string, bool> _heads = [];
    private readonly Dictionary<string, StoredPage> _revisions = [];

    public Exception? Failure { get; init; }

    /// <summary>
    /// Lets a test simulate a save that lands while a page is compiling: the replacement is stored
    /// and the conditional write is refused. Runs once.
    /// </summary>
    public Func<PagePath, string, StoredPage?>? ReplaceOnCompilationWrite { get; set; }

    public bool DeleteOnCompilationWrite { get; set; }

    public int ReadCount { get; private set; }

    public int WriteCount { get; private set; }

    public int CompilationWriteCount { get; private set; }

    /// <summary>Spends time in every read, so a test can exhaust a render's budget before it runs.</summary>
    public TimeSpan ReadDelay { get; init; }

    public PagePath? LastReadPath { get; private set; }

    public PagePath? LastWritePath { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public StoredPage? this[string path] {
        get => _pages.GetValueOrDefault(PagePath.Normalize(path).Key);
        set {
            StoredPage page = value ?? throw new ArgumentNullException(nameof(value));
            _pages[PagePath.Normalize(path).Key] = page;
            _heads[PagePath.Normalize(path).Key] = false;
            _revisions[page.Hash] = page;
        }
    }

    public async Task<StoredPage?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);

        ReadCount++;
        LastReadPath = path;

        if (ReadDelay > TimeSpan.Zero) {
            await Task.Delay(ReadDelay, cancellationToken);
        }

        return _heads.GetValueOrDefault(path.Key) ? null : _pages.GetValueOrDefault(path.Key);
    }

    public Task<IReadOnlyList<PageHeadSummary>> ListPagesAsync(CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        IReadOnlyList<PageHeadSummary> result = [.. _heads
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PageHeadSummary(PagePath.Normalize(item.Key), item.Value))];
        return Task.FromResult(result);
    }

    public Task<PageHeadSummary?> GetHeadAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        return Task.FromResult(_heads.TryGetValue(path.Key, out bool isDeleted)
            ? new PageHeadSummary(path, isDeleted)
            : null);
    }

    public Task<StoredPage?> GetRevisionByHashAsync(string hash, CancellationToken cancellationToken = default) {
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
                page.Compilation is null ? "Not compiled" : page.Compilation.Image is null ? "Compile failed" : "Compiled",
                page.RevisionId == currentRevisionId))];
        return Task.FromResult(revisions);
    }

    public Task<PageRevisionSource?> GetRevisionSourceByHashAsync(string hash, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        StoredPage? page = _revisions.GetValueOrDefault(hash);
        return Task.FromResult(page is null
            ? null
            : new PageRevisionSource(page.Path, page.Hash, page.SavedAt, page.CreationRuntimeId,
                page.Compilation is null ? "Not compiled" : page.Compilation.Image is null ? "Compile failed" : "Compiled",
                page.Content));
    }

    public Task SavePageAsync(StoredPage page, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);
        Check(cancellationToken);

        WriteCount++;
        LastWritePath = page.Path;
        _pages[page.Path.Key] = page;
        _heads[page.Path.Key] = false;
        _revisions[page.Hash] = page;

        return Task.CompletedTask;
    }

    public Task<bool> TrySetCompilationAsync(string revisionId, string expectedContent, PageCompilation compilation, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(compilation);
        Check(cancellationToken);

        CompilationWriteCount++;
        StoredPage? target = _revisions.Values.SingleOrDefault(page => page.RevisionId == revisionId);
        if (target is null) {
            return Task.FromResult(false);
        }

        PagePath path = target.Path;
        LastWritePath = path;

        if (ReplaceOnCompilationWrite?.Invoke(path, expectedContent) is { } replacement) {
            ReplaceOnCompilationWrite = null;
            _pages[path.Key] = replacement;
            _heads[path.Key] = false;
            _revisions[replacement.Hash] = replacement;
            return Task.FromResult(false);
        }

        if (DeleteOnCompilationWrite) {
            DeleteOnCompilationWrite = false;
            _heads[path.Key] = true;
        }

        if (!string.Equals(target.Content, expectedContent, StringComparison.Ordinal)) {
            return Task.FromResult(false);
        }

        StoredPage compiled = target with { Compilation = compilation };
        _revisions[compiled.Hash] = compiled;
        if (_pages.GetValueOrDefault(path.Key)?.RevisionId == revisionId) {
            _pages[path.Key] = compiled;
        }
        return Task.FromResult(true);
    }

    public Task<bool> DeletePageAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        if (!_heads.TryGetValue(path.Key, out bool isDeleted) || isDeleted) {
            return Task.FromResult(false);
        }

        _heads[path.Key] = true;
        return Task.FromResult(true);
    }

    public Task<bool> ScrubPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);
        bool removed = _heads.Remove(path.Key) | _pages.Remove(path.Key);
        foreach (string hash in _revisions.Where(item => item.Value.Path == path).Select(item => item.Key).ToArray()) {
            removed |= _revisions.Remove(hash);
        }
        return Task.FromResult(removed);
    }

    private void Check(CancellationToken cancellationToken) {
        LastCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        if (Failure is not null) {
            throw Failure;
        }
    }
}
