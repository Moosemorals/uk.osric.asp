// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

/// <summary>An in-memory session for handlers driven through a hand-built context.</summary>
internal sealed class TestSession : ISession {

    private readonly Dictionary<string, byte[]> _values = [];

    public bool IsAvailable => true;

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public IEnumerable<string> Keys => _values.Keys;

    public void Clear() {
        _values.Clear();
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) {
        return Task.CompletedTask;
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) {
        return Task.CompletedTask;
    }

    public void Remove(string key) {
        _values.Remove(key);
    }

    public void Set(string key, byte[] value) {
        _values[key] = value;
    }

    public bool TryGetValue(string key, out byte[] value) {
        return _values.TryGetValue(key, out value!);
    }
}

/// <summary>Installs a <see cref="TestSession"/> on a hand-built context.</summary>
internal sealed class TestSessionFeature : ISessionFeature {

    public ISession Session { get; set; } = new TestSession();
}
