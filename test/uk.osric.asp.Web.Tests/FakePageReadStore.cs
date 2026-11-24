// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Runtime.CompilerServices;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
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
    /// <param name="hash">
    /// The revision's hash, when a test needs the catalog to agree with another store about it; the
    /// store invents one from the key and position otherwise.
    /// </param>
    /// <returns>The revision's hash.</returns>
    public string Save(
        string key,
        string template,
        DateTimeOffset? savedAt = null,
        PageKind? templateKind = null,
        string? runtimeId = null,
        bool hasImage = false,
        bool hasDiagnostics = false,
        string? hash = null,
        string? seedHash = null,
        string? contentHash = null,
        string? templateLibraries = null) {
        DateTimeOffset when = savedAt ?? _clock.AddMinutes(1);
        _clock = when > _clock ? when : _clock;

        if (!_pages.TryGetValue(key, out List<FakeRevision>? revisions)) {
            revisions = _pages[key] = [];
        }

        hash ??= "hash-" + revisions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + key;
        contentHash ??= TemplateFingerprint.Of(template);
        revisions.Add(new FakeRevision(
            hash, when, template, templateKind, runtimeId, hasImage, hasDiagnostics, seedHash, contentHash,
            templateLibraries));

        return hash;
    }

    /// <summary>Turns a page into a tombstone, keeping its history.</summary>
    public void Delete(string key) => _deleted.Add(key);

    /// <inheritdoc/>
    public Task<IReadOnlyList<PageReadHead>> ListLibrariesAsync(CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;

        return Task.FromResult<IReadOnlyList<PageReadHead>>([
            .. _pages
                .Where(item => !_deleted.Contains(item.Key))
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => (Key: item.Key, Revision: item.Value[^1]))
                .Where(item => item.Revision.TemplateKind is PageKind.Library)
                .Select(item => new PageReadHead(
                    item.Key, item.Revision.SavedAt, item.Revision.Hash, IsDeleted: false,
                    item.Revision.TemplateKind, item.Revision.RuntimeId,
                    item.Revision.HasImage, item.Revision.HasDiagnostics,
                    item.Revision.SeedHash, item.Revision.ContentHash,
                    SourceBytes: System.Text.Encoding.UTF8.GetByteCount(item.Revision.Template))),
        ]);
    }

    /// <inheritdoc/>
    public async Task<PageReadSlice> ListAsync(
        string prefix, string? after, int limit, bool recursive = true, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        PagePath at = PagePath.Normalize(prefix);

        return new PageReadSlice(
            [.. _pages
                .Where(item => InSubtree(item.Key, prefix))
                .Where(item => recursive || IsSelfOrDirectChild(item.Key, at))
                .Where(item => after is null || string.CompareOrdinal(item.Key, after) > 0)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Take(limit)
                .Select(item => {
                    FakeRevision latest = item.Value[^1];
                    return new PageReadHead(
                        item.Key, latest.SavedAt, latest.Hash, _deleted.Contains(item.Key), latest.TemplateKind,
                        latest.RuntimeId, latest.HasImage, latest.HasDiagnostics, latest.SeedHash, latest.ContentHash,
                        latest.TemplateLibraries, System.Text.Encoding.UTF8.GetByteCount(latest.Template));
                })],
            PageSubtreeFingerprint.Of(Facts(prefix)));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListFoldersAsync(string prefix, string? after, int limit, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        PagePath at = PagePath.Normalize(prefix);
        SortedSet<string> folders = [with(StringComparer.Ordinal)];

        foreach (string key in _pages.Keys) {
            if (!PagePath.Normalize(key).IsBelow(at)) {
                continue;
            }

            // Folders above a key are outermost first, so the one directly under the prefix is the
            // child the real statement folds the key into.
            foreach (string folder in PageTree.FoldersAbove(key)) {
                if (PagePath.Normalize(folder).IsDirectlyBelow(at)) {
                    _ = folders.Add(folder);
                    break;
                }
            }
        }

        return [.. folders
            .Where(folder => after is null || string.CompareOrdinal(folder, after) > 0)
            .Take(limit)];
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<PageReadSource> ScanAsync(
        string prefix,
        string? from,
        bool recursive = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        PagePath at = PagePath.Normalize(prefix);

        foreach (string key in _pages.Keys.OrderBy(key => key, StringComparer.Ordinal)) {
            if (_deleted.Contains(key)
                || !InSubtree(key, prefix)
                || !(recursive || IsSelfOrDirectChild(key, at))
                || (from is not null && string.CompareOrdinal(key, from) < 0)) {
                continue;
            }

            yield return Source(key, _pages[key][^1]);
        }
    }

    /// <inheritdoc/>
    public async Task<string> VersionAsync(string prefix, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        return PageSubtreeFingerprint.Of(Facts(prefix));
    }

    /// <inheritdoc/>
    public async Task<PageReadHead?> GetHeadAsync(string key, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        ReadCount++;
        await Task.Delay(ReadDelay, cancellationToken);

        if (!_pages.TryGetValue(key, out List<FakeRevision>? revisions)) {
            return null;
        }

        FakeRevision latest = revisions[^1];

        return new PageReadHead(
            key, latest.SavedAt, latest.Hash, _deleted.Contains(key), latest.TemplateKind,
            latest.RuntimeId, latest.HasImage, latest.HasDiagnostics, latest.SeedHash, latest.ContentHash);
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
                revision.Hash, revision.SavedAt, live && ReferenceEquals(revision, current),
                System.Text.Encoding.UTF8.GetByteCount(revision.Template)))];
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

    private PageFingerprintFact[] Facts(string prefix) =>
        [.. _pages
            .Where(item => InSubtree(item.Key, prefix))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new PageFingerprintFact(
                item.Key,
                item.Value[^1].Hash,
                _deleted.Contains(item.Key),
                item.Value[^1].SeedHash,
                item.Value[^1].ContentHash))];

    private static bool IsSelfOrDirectChild(string key, PagePath at) {
        PagePath path = PagePath.Normalize(key);

        return string.Equals(path.Key, at.Key, StringComparison.Ordinal) || path.IsDirectlyBelow(at);
    }

    private static bool InSubtree(string key, string prefix) =>
        prefix == "/"
            || string.Equals(key, prefix, StringComparison.Ordinal)
            || key.StartsWith(prefix + "/", StringComparison.Ordinal);

    private sealed record FakeRevision(
        string Hash,
        DateTimeOffset SavedAt,
        string Template,
        PageKind? TemplateKind,
        string? RuntimeId,
        bool HasImage,
        bool HasDiagnostics,
        string? SeedHash,
        string? ContentHash,
        string? TemplateLibraries);
}
