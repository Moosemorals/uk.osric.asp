// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IPageSessionStore"/> that applies the same rules as the real one.</summary>
/// <remarks>
/// The identity, the eviction order and the sweep are enforced here too, so a test proves the behaviour
/// the store promises without needing a database. The SQL is proven separately, against the real store.
/// The values live in <see cref="FakeStoreValues"/>, which is where a session's subject is removed from.
/// </remarks>
internal sealed class FakePageSessionStore(FakeStoreValues? values = null, VisitorOptions? options = null) : IPageSessionStore {

    private readonly Dictionary<string, Row> _sessions = [];
    private readonly VisitorOptions _options = options ?? TestOptions.Stores.Visitor;

    /// <summary>The caps and lifetimes this store enforces.</summary>
    public VisitorOptions Options => _options;

    /// <summary>Every session the store holds.</summary>
    public IReadOnlyCollection<string> Ids => _sessions.Keys;

    /// <summary>The clock the sweep compares against.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>The token the last call was given, so a test can prove the render's deadline arrives.</summary>
    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>The values a session's subject is removed from, when the test wired one.</summary>
    public FakeStoreValues? Values => values;

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

    public async Task<bool> AdoptAsync(
        string? previousSessionId,
        string account,
        string sessionId,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        if (previousSessionId is not null) {
            values?.Adopt(previousSessionId, account);
            values?.DeleteSubject(StoreSubjectKeys.ForSession(previousSessionId));
            _ = _sessions.Remove(previousSessionId);
        }

        while (_sessions.Count >= _options.MaxLiveSessions) {
            (int anonymous, int signedIn) = Evict();
            if (anonymous + signedIn == 0) {
                return false;
            }
        }

        _sessions[sessionId] = new Row(account, Clock.GetUtcNow());
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

        values?.DeleteSubject(StoreSubjectKeys.ForSession(sessionId));
        return _sessions.Remove(sessionId);
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

    /// <summary>The only session the store holds, for a test that made exactly one request.</summary>
    public string Single() {
        Assert.That(_sessions.Keys, Has.Exactly(1).Items, "exactly one session was expected");

        return _sessions.Keys.Single();
    }

    private int RemoveExpired(DateTimeOffset now) {
        List<string> expired = [.. _sessions
            .Where(pair => pair.Value.UpdatedAt < now - _options.AnonymousIdle)
            .Select(pair => pair.Key)];

        foreach (string id in expired) {
            values?.DeleteSubject(StoreSubjectKeys.ForSession(id));
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
        values?.DeleteSubject(StoreSubjectKeys.ForSession(victim.Key));

        return victim.Value.Account is null ? (1, 0) : (0, 1);
    }

    private sealed class Row(string? account, DateTimeOffset now) {
        public string? Account { get; } = account;

        public DateTimeOffset CreatedAt { get; } = now;

        public DateTimeOffset UpdatedAt { get; set; } = now;
    }
}
