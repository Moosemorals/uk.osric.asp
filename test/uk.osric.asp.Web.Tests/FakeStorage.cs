// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core;
using uk.osric.asp.Web;

/// <summary>An in-memory <see cref="IStorage"/> that records what the shell asked of it.</summary>
internal sealed class FakeStorage : IStorage {
    private readonly Dictionary<string, StoredPage> _pages = [];

    public Exception? Failure { get; init; }

    /// <summary>
    /// Lets a test simulate a save that lands while a page is compiling: the replacement is stored
    /// and the conditional write is refused. Runs once.
    /// </summary>
    public Func<PagePath, string, StoredPage?>? ReplaceOnCompilationWrite { get; set; }

    public int ReadCount { get; private set; }

    public int WriteCount { get; private set; }

    public int CompilationWriteCount { get; private set; }

    /// <summary>Spends time in every read, so a test can exhaust a render's budget before it runs.</summary>
    public TimeSpan ReadDelay { get; init; }

    public PagePath? LastReadPath { get; private set; }

    public PagePath? LastWritePath { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public StoredPage? this[string path] {
        get => _pages.GetValueOrDefault(PagePath.Normalize(path).Path);
        set => _pages[PagePath.Normalize(path).Path] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public async Task<StoredPage?> GetPageAsync(PagePath path, CancellationToken cancellationToken = default) {
        Check(cancellationToken);

        ReadCount++;
        LastReadPath = path;

        if (ReadDelay > TimeSpan.Zero) {
            await Task.Delay(ReadDelay, cancellationToken);
        }

        return _pages.GetValueOrDefault(path.Path);
    }

    public Task SavePageAsync(StoredPage page, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(page);
        Check(cancellationToken);

        WriteCount++;
        LastWritePath = page.Path;
        _pages[page.Path.Path] = page;

        return Task.CompletedTask;
    }

    public Task<bool> TrySetCompilationAsync(PagePath path, string expectedContent, PageCompilation compilation, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(compilation);
        Check(cancellationToken);

        CompilationWriteCount++;
        LastWritePath = path;

        if (ReplaceOnCompilationWrite?.Invoke(path, expectedContent) is { } replacement) {
            ReplaceOnCompilationWrite = null;
            _pages[path.Path] = replacement;
            return Task.FromResult(false);
        }

        if (_pages.GetValueOrDefault(path.Path) is not { } existing
            || !string.Equals(existing.Content, expectedContent, StringComparison.Ordinal)) {
            return Task.FromResult(false);
        }

        _pages[path.Path] = existing with { Compilation = compilation };
        return Task.FromResult(true);
    }

    private void Check(CancellationToken cancellationToken) {
        LastCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        if (Failure is not null) {
            throw Failure;
        }
    }
}
