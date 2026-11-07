// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using System.Security.Cryptography;

/// <summary>Holds the reviewed archives, expiring them on a time-to-live and a count.</summary>
/// <remarks>
/// A stage lives in memory only. The oldest is evicted when a new one would exceed
/// <see cref="ArchiveOptions.MaxStagedImports"/>, and a stage older than
/// <see cref="ArchiveOptions.StageTtl"/> is gone by the time anybody asks for it, so an abandoned
/// upload cannot sit in memory indefinitely.
/// </remarks>
internal sealed class ImportStageStore(ArchiveOptions options, TimeProvider clock) {

    /// <summary>The length of a stage identifier: 32 hexadecimal characters.</summary>
    public const int IdLength = 32;

    private readonly Dictionary<string, ImportStage> _stages = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>How many stages are held; a fixture uses it to prove a rejection staged nothing.</summary>
    public int Count {
        get {
            lock (_lock) {
                EvictExpired();
                return _stages.Count;
            }
        }
    }

    /// <summary>Stages a validated archive under a fresh identifier.</summary>
    /// <param name="pages">The validated pages.</param>
    /// <returns>The new stage.</returns>
    public ImportStage Create(IReadOnlyList<PageArchiveEntry> pages) {
        ArgumentNullException.ThrowIfNull(pages);

        string id = RandomNumberGenerator.GetHexString(IdLength, lowercase: true);
        ImportStage stage = new(id, pages, clock.GetUtcNow());

        lock (_lock) {
            EvictExpired();
            while (_stages.Count >= options.MaxStagedImports) {
                string oldest = _stages.MinBy(item => item.Value.CreatedAt).Key;
                _stages.Remove(oldest);
            }

            _stages[id] = stage;
        }

        return stage;
    }

    /// <summary>Finds a live stage.</summary>
    /// <param name="id">The identifier from the URL.</param>
    /// <returns>The stage, or <see langword="null"/> when it is unknown, malformed or expired.</returns>
    public ImportStage? Get(string? id) {
        if (!IsWellFormed(id)) {
            return null;
        }

        lock (_lock) {
            EvictExpired();
            return _stages.GetValueOrDefault(id!);
        }
    }

    /// <summary>Whether text could be a stage identifier at all.</summary>
    /// <param name="id">The text from the URL.</param>
    /// <remarks>
    /// Checked before the dictionary is consulted, so a crafted path cannot reach a lookup and the
    /// route answers <c>404</c> for anything a stage could never be named.
    /// </remarks>
    public static bool IsWellFormed(string? id) =>
        id is { Length: IdLength } && id.All(Uri.IsHexDigit);

    private void EvictExpired() {
        DateTimeOffset cutoff = clock.GetUtcNow() - options.StageTtl;
        foreach (string id in _stages
            .Where(item => item.Value.CreatedAt <= cutoff)
            .Select(item => item.Key)
            .ToArray()) {
            _stages.Remove(id);
        }
    }
}
