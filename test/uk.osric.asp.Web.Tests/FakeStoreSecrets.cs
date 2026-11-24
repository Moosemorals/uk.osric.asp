// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IStoreSecrets"/> for the capability tests.</summary>
internal sealed class FakeStoreSecrets : IStoreSecrets {

    private readonly List<StoreSecretRow> _rows = [];

    /// <summary>Every row, for a test that wants to prove what a page holds.</summary>
    public IReadOnlyList<StoreSecretRow> Rows => _rows;

    /// <summary>How many times a write reached the store.</summary>
    public int WriteCount { get; private set; }

    /// <summary>Adds a row directly, as a previous render would have.</summary>
    public StoreSecretRow Add(string subject, string pageKey, string name, string value) {
        StoreSecretRow row = new(subject, pageKey, name, value, "2026-01-01T00:00:00.0000000Z");
        _ = _rows.RemoveAll(item => item.Subject == subject && item.Name == name);
        _rows.Add(row);

        return row;
    }

    /// <inheritdoc/>
    public Task<StoreSecretRow?> FindAsync(
        string subject,
        string name,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;

        return Task.FromResult(_rows.FirstOrDefault(
            row => row.Subject == subject && row.Name == name));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<StoreSecretRow>> ListAsync(
        string subject,
        int limit,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;

        IReadOnlyList<StoreSecretRow> rows = [.. _rows
            .Where(row => row.Subject == subject)
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .Take(limit)];

        return Task.FromResult(rows);
    }

    /// <inheritdoc/>
    public Task WriteAsync(
        StoreSecretWrite write,
        PageSecretOptions limits,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        _ = limits;
        WriteCount++;
        _ = Add(write.Subject, write.PageKey, write.Name, write.Value);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(
        string subject,
        string name,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        int removed = _rows.RemoveAll(row => row.Subject == subject && row.Name == name);

        return Task.FromResult(removed > 0);
    }
}
