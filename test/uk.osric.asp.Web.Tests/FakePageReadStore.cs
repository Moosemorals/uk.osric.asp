// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IPageReadStore"/> that applies the same rules as the real one.</summary>
/// <remarks>
/// Keys are canonical strings, a listing selects a subtree, and a tombstone keeps its history but
/// has no live source. The SQL is proven separately, against the real store.
/// </remarks>
internal sealed class FakePageReadStore : IPageReadStore {

    private readonly Dictionary<string, List<FakeRevision>> _pages = [];
    private readonly HashSet<string> _deleted = [];
    private DateTimeOffset _clock = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>How long a read takes, so a test can leave a page suspended in an <c>await</c>.</summary>
    public TimeSpan ReadDelay { get; set; }

    public int ReadCount { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>Stores a new revision, as a save would.</summary>
    /// <param name="key">The page's canonical key.</param>
    /// <param name="template">The template text.</param>
    /// <param name="savedAt">The revision's time; the store's own clock is used when omitted.</param>
    /// <returns>The revision's hash.</returns>
    public string Save(
        string key,
        string template,
        DateTimeOffset? savedAt = null,
        bool? templateHasCode = null,
        string? runtimeId = null,
        bool hasImage = false,
        bool hasDiagnostics = false) {
        DateTimeOffset when = savedAt ?? _clock.AddMinutes(1);
        _clock = when > _clock ? when : _clock;

        if (!_pages.TryGetValue(key, out List<FakeRevision>? revisions)) {
            revisions = _pages[key] = [];
        }

        string hash = "hash-" + revisions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + key;
        revisions.Add(new FakeRevision(hash, when, template, templateHasCode, runtimeId, hasImage, hasDiagnostics));

        return hash;
    }

    /// <summary>Turns a page into a tombstone, keeping its history.</summary>
    public void Delete(string key) => _deleted.Add(key);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageReadHead>> ListAsync(string prefix, int limit, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        return [.. _pages
            .Where(item => InSubtree(item.Key, prefix))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(item => {
                FakeRevision latest = item.Value[^1];
                return new PageReadHead(
                    item.Key, latest.SavedAt, latest.Hash, _deleted.Contains(item.Key), latest.TemplateHasCode,
                    latest.RuntimeId, latest.HasImage, latest.HasDiagnostics);
            })];
    }

    /// <inheritdoc/>
    public async Task<PageReadSource?> GetSourceAsync(string key, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        return !_deleted.Contains(key) && _pages.TryGetValue(key, out List<FakeRevision>? revisions)
            ? Source(key, revisions[^1])
            : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageReadRevision>> ListRevisionsAsync(string key, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        if (!_pages.TryGetValue(key, out List<FakeRevision>? revisions)) {
            return [];
        }

        FakeRevision current = revisions[^1];
        bool live = !_deleted.Contains(key);

        return [.. revisions
            .OrderByDescending(revision => revision.SavedAt)
            .ThenByDescending(revision => revision.Hash, StringComparer.Ordinal)
            .Select(revision => new PageReadRevision(
                revision.Hash, revision.SavedAt, live && ReferenceEquals(revision, current)))];
    }

    /// <inheritdoc/>
    public async Task<PageReadSource?> GetRevisionSourceAsync(string key, string hash, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        return _pages.TryGetValue(key, out List<FakeRevision>? revisions)
            && revisions.Find(revision => string.Equals(revision.Hash, hash, StringComparison.Ordinal)) is { } match
                ? Source(key, match)
                : null;
    }

    private static PageReadSource Source(string key, FakeRevision revision) =>
        new(key, revision.Hash, revision.SavedAt, revision.Template);

    private static bool InSubtree(string key, string prefix) =>
        prefix == "/"
            || string.Equals(key, prefix, StringComparison.Ordinal)
            || key.StartsWith(prefix + "/", StringComparison.Ordinal);

    private sealed record FakeRevision(
        string Hash,
        DateTimeOffset SavedAt,
        string Template,
        bool? TemplateHasCode,
        string? RuntimeId,
        bool HasImage,
        bool HasDiagnostics);
}
