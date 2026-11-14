// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IPageSessionStore"/> that applies the same rules as the real one.</summary>
/// <remarks>
/// The per-session caps, the eviction order and the sweep are enforced here too, so a test proves the
/// behaviour the store promises without needing a database. The SQL is proven separately, against the
/// real store.
/// </remarks>
internal sealed class FakePageSessionStore(PageSessionOptions? options = null) : IPageSessionStore {

    private readonly Dictionary<string, Row> _sessions = [];
    private readonly PageSessionOptions _options = options ?? TestOptions.Session;

    /// <summary>The caps this store enforces.</summary>
    public PageSessionOptions Options => _options;

    /// <summary>Every session the store holds, oldest minted first.</summary>
    public IReadOnlyCollection<string> Ids => _sessions.Keys;

    /// <summary>The clock the sweep compares against.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>The token the last call was given, so a test can prove the render's deadline arrives.</summary>
    public CancellationToken LastCancellationToken { get; private set; }

    public async Task<PageSessionEntry?> FindAsync(string sessionId, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        return _sessions.TryGetValue(sessionId, out Row? row)
            ? new PageSessionEntry {
                SessionId = sessionId,
                Account = row.Account,
                CreatedAt = row.CreatedAt.UtcDateTime,
                UpdatedAt = row.UpdatedAt.UtcDateTime,
            }
            : null;
    }

    public async Task<bool> EnsureAsync(string sessionId, string? account, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        if (_sessions.ContainsKey(sessionId)) {
            return true;
        }

        DateTimeOffset now = Clock.GetUtcNow();
#pragma warning disable CA1849 // The fake sweeps a dictionary; nothing here can block.
        _ = RemoveExpired(now);
#pragma warning restore CA1849

        while (_sessions.Count >= _options.MaxLiveSessions) {
            (int anonymous, int signedIn) = Evict();
            if (anonymous + signedIn == 0) {
                return false;
            }
        }

        _sessions[sessionId] = new Row(account, now);
        return true;
    }

    public async Task TouchAsync(string sessionId, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        if (_sessions.TryGetValue(sessionId, out Row? row)) {
            row.UpdatedAt = Clock.GetUtcNow();
        }
    }

    public async Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default) {
        await Task.Yield();

        return _sessions.Remove(sessionId);
    }

    public async Task<string?> GetValueAsync(
        string sessionId,
        string pagePath,
        string key,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        return _sessions.TryGetValue(sessionId, out Row? row)
            ? row.Values.GetValueOrDefault((pagePath, key))
            : null;
    }

    public async Task SetValueAsync(
        string sessionId,
        string pagePath,
        string key,
        string value,
        PageSessionCaps caps,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        if (!_sessions.TryGetValue(sessionId, out Row? row)) {
            throw new ArgumentException("The session is no longer live.");
        }

        bool existing = row.Values.ContainsKey((pagePath, key));
        if (!existing && row.Values.Count >= caps.MaxKeys) {
            throw new ArgumentException($"The session already holds {row.Values.Count} keys and may hold {caps.MaxKeys}.");
        }

        long total = row.Values
            .Where(pair => pair.Key != (pagePath, key))
            .Sum(pair => (long)pair.Key.Key.Length + pair.Value.Length);
        total += key.Length + value.Length;
        if (total > caps.MaxTotalBytes) {
            throw new ArgumentException($"The session would need {total} bytes of state and may use {caps.MaxTotalBytes}.");
        }

        row.Values[(pagePath, key)] = value;
    }

    public async Task<bool> DeleteValueAsync(
        string sessionId,
        string pagePath,
        string key,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        return _sessions.TryGetValue(sessionId, out Row? row) && row.Values.Remove((pagePath, key));
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(
        string sessionId,
        string pagePath,
        CancellationToken cancellationToken = default) {
        await Task.Yield();

        if (!_sessions.TryGetValue(sessionId, out Row? row)) {
            return [];
        }

        List<string> keys = [.. row.Values.Keys
            .Where(entry => string.Equals(entry.Page, pagePath, StringComparison.Ordinal))
            .Select(entry => entry.Key)];
        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    public async Task<IReadOnlyList<PageSessionKey>> ListAllKeysAsync(
        string sessionId,
        CancellationToken cancellationToken = default) {
        await Task.Yield();

        if (!_sessions.TryGetValue(sessionId, out Row? row)) {
            return [];
        }

        return [.. row.Values.Keys
            .OrderBy(entry => entry.Page, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new PageSessionKey(entry.Page, entry.Key))];
    }

    public async Task<PageSessionSweep> RemoveExpiredAsync(CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        int expired = RemoveExpired(Clock.GetUtcNow());
        int anonymous = 0;
        int signedIn = 0;

        while (_sessions.Count > _options.MaxLiveSessions) {
            (int evictedAnonymous, int evictedSignedIn) = Evict();
            if (evictedAnonymous + evictedSignedIn == 0) {
                break;
            }

            anonymous += evictedAnonymous;
            signedIn += evictedSignedIn;
        }

        return new PageSessionSweep(expired, anonymous, signedIn);
    }

    /// <summary>Mints a session directly, as a cookie-bearing request would.</summary>
    /// <remarks>The id is a real cookie value, so a test can present it to the endpoint.</remarks>
    public string Create(string? account = null) {
        string id = uk.osric.asp.Web.Authentication.PageSessionCookie.Mint();
        _sessions[id] = new Row(account, Clock.GetUtcNow());

        return id;
    }

    /// <summary>Puts a value in without the caps, as a direct database write would.</summary>
    public void Seed(string sessionId, string pagePath, string key, string json) {
        if (!_sessions.TryGetValue(sessionId, out Row? row)) {
            row = _sessions[sessionId] = new Row(null, Clock.GetUtcNow());
        }

        row.Values[(pagePath, key)] = json;
    }

    /// <summary>Puts a string in, as the shell's own flash does.</summary>
    public void SeedText(string sessionId, string pagePath, string key, string text) =>
        Seed(sessionId, pagePath, key, System.Text.Json.JsonSerializer.Serialize(text));

    /// <summary>Reads the string one key holds, decoded from its JSON.</summary>
    public string? Text(string sessionId, string pagePath, string key) {
        string? json = Raw(sessionId, pagePath, key);

        return json is null ? null : System.Text.Json.JsonSerializer.Deserialize<string>(json);
    }

    /// <summary>The one string any session holds under <paramref name="pagePath"/> and <paramref name="key"/>.</summary>
    public string? OnlyText(string pagePath, string key) =>
        _sessions.Keys
            .Select(id => Text(id, pagePath, key))
            .SingleOrDefault(text => text is not null);

    /// <summary>Reads the raw JSON one key holds.</summary>
    public string? Raw(string sessionId, string pagePath, string key) =>
        _sessions.TryGetValue(sessionId, out Row? row) ? row.Values.GetValueOrDefault((pagePath, key)) : null;

    /// <summary>The only session the store holds, for a test that made exactly one request.</summary>
    public string Single() {
        Assert.That(_sessions.Keys, Has.Exactly(1).Items, "exactly one session was expected");

        return _sessions.Keys.Single();
    }

    /// <summary>How many values every session holds together.</summary>
    public int ValueCount => _sessions.Values.Sum(row => row.Values.Count);

    private int RemoveExpired(DateTimeOffset now) {
        List<string> expired = [.. _sessions
            .Where(pair => pair.Value.UpdatedAt < now - _options.IdleFor(pair.Value.Account))
            .Select(pair => pair.Key)];

        foreach (string id in expired) {
            _ = _sessions.Remove(id);
        }

        return expired.Count;
    }

    // Anonymous sessions go first, oldest first: the same order the SQL store evicts in.
    private (int Anonymous, int SignedIn) Evict() {
        if (_sessions.Count == 0) {
            return (0, 0);
        }

        KeyValuePair<string, Row> victim = _sessions
            .OrderBy(pair => pair.Value.Account is null ? 0 : 1)
            .ThenBy(pair => pair.Value.UpdatedAt)
            .First();

        _ = _sessions.Remove(victim.Key);

        return victim.Value.Account is null ? (1, 0) : (0, 1);
    }

    private sealed class Row(string? account, DateTimeOffset now) {
        public string? Account { get; } = account;

        public DateTimeOffset CreatedAt { get; } = now;

        public DateTimeOffset UpdatedAt { get; set; } = now;

        public Dictionary<(string Page, string Key), string> Values { get; } = [];
    }
}
