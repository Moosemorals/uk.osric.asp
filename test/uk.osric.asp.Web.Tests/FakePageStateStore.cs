// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IPageStateStore"/> that applies the same rules as the real one.</summary>
/// <remarks>
/// The per-page caps are enforced here too, so a test proves the behaviour the store promises without
/// needing a database. The SQL is proven separately, against the real store.
/// </remarks>
internal sealed class FakePageStateStore(PageStorageOptions? options = null) : IPageStateStore {

    private readonly Dictionary<string, Dictionary<string, string>> _pages = new(StringComparer.Ordinal);
    private readonly PageStorageOptions _options = options ?? new PageStorageOptions();

    /// <summary>How long a read takes, so a test can leave a page suspended in an <c>await</c>.</summary>
    public TimeSpan ReadDelay { get; set; }

    /// <summary>Pages whose head is live; a write to any other page is refused.</summary>
    public HashSet<string> LivePages { get; } = new(StringComparer.Ordinal);

    public int WriteCount { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public async Task<string?> GetValueAsync(PagePath path, string key, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Delay(ReadDelay, cancellationToken);

        return _pages.TryGetValue(path.Key, out Dictionary<string, string>? keys)
            ? keys.GetValueOrDefault(key)
            : null;
    }

    public async Task SetValueAsync(PagePath path, string key, string value, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        if (!LivePages.Contains(path.Key)) {
            throw new ArgumentException($"{path.Key} was deleted before its state could be written.", nameof(key));
        }

        Dictionary<string, string> keys = _pages.TryGetValue(path.Key, out Dictionary<string, string>? existing)
            ? existing
            : _pages[path.Key] = new Dictionary<string, string>(StringComparer.Ordinal);

        bool isNew = !keys.ContainsKey(key);
        if (isNew && keys.Count >= _options.MaxKeysPerPage) {
            throw new ArgumentException($"{path.Key} already holds {keys.Count} keys.", nameof(key));
        }

        long total = keys
            .Where(row => !string.Equals(row.Key, key, StringComparison.Ordinal))
            .Sum(row => (long)row.Key.Length + row.Value.Length);
        total += key.Length + value.Length;
        if (total > _options.MaxPageBytes) {
            throw new ArgumentException($"{path.Key} would need {total} bytes of state.", nameof(key));
        }

        keys[key] = value;
        WriteCount++;
    }

    public async Task<bool> DeleteValueAsync(PagePath path, string key, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Delay(ReadDelay, cancellationToken);

        return _pages.TryGetValue(path.Key, out Dictionary<string, string>? keys) && keys.Remove(key);
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(PagePath path, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        if (!_pages.TryGetValue(path.Key, out Dictionary<string, string>? keys)) {
            return [];
        }

        List<string> result = [.. keys.Keys];
        result.Sort(StringComparer.Ordinal);

        return result;
    }

    /// <summary>Puts a value in without the caps, as a direct database write would.</summary>
    public void Seed(string pagePath, string key, string value) {
        if (!_pages.TryGetValue(pagePath, out Dictionary<string, string>? keys)) {
            keys = _pages[pagePath] = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        keys[key] = value;
        LivePages.Add(pagePath);
    }
}
