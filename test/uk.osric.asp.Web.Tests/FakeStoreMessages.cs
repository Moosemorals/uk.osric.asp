// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text;
using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IStoreMessages"/> that applies the same rules as the real one.</summary>
/// <remarks>
/// The subject filter, the address, the FIFO order, the expiry and the caps are enforced here too, so
/// a render test proves the behaviour the store promises without a database. The SQL is proven
/// separately, against the real store.
/// </remarks>
internal sealed class FakeStoreMessages(TestClock? clock = null) : IStoreMessages {

    private readonly List<StoreMessageRow> _rows = [];
    private readonly TestClock _clock = clock ?? new TestClock();
    private long _nextId;

    /// <summary>Every row, for a test that wants to prove what a subject holds.</summary>
    public IReadOnlyList<StoreMessageRow> Rows => _rows;

    public int WriteCount { get; private set; }

    public Task<bool> PostAsync(
        StoreMessageWrite write,
        PageMessageOptions caps,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        int held = _rows.Count(row => row.Subject == write.Subject);
        int bytes = _rows.Where(row => row.Subject == write.Subject).Sum(row => Encoding.UTF8.GetByteCount(row.Body));
        int added = Encoding.UTF8.GetByteCount(write.Body);

        if (held >= caps.MaxMessagesPerSubject || bytes + added > caps.MaxSubjectBytes) {
            return Task.FromResult(false);
        }

        _rows.Add(new StoreMessageRow(
            ++_nextId,
            write.Subject,
            write.ToPageKey,
            write.Topic,
            write.FromPageKey,
            write.Body,
            write.Lifetime,
            write.ExpiresAt,
            StoreSubjectKeys.Stamp(_clock.GetUtcNow())));
        WriteCount++;

        return Task.FromResult(true);
    }

    public Task<StoreMessageRow?> TakeAsync(
        IReadOnlyList<string> subjects,
        string? toPageKey,
        string? topic,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        StoreMessageRow? row = Find(subjects, toPageKey, topic);
        if (row is not null) {
            _ = _rows.Remove(row);
            WriteCount++;
        }

        return Task.FromResult(row);
    }

    public Task<StoreMessageRow?> PeekAsync(
        IReadOnlyList<string> subjects,
        string? toPageKey,
        string? topic,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;

        return Task.FromResult(Find(subjects, toPageKey, topic));
    }

    public Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        DateTimeOffset now = _clock.GetUtcNow();
        int expired = _rows.RemoveAll(row => !IsLive(row, now));

        string threshold = StoreSubjectKeys.Stamp(now - browserLifetime);
        string[] stale = [.. _rows
            .Where(row => StoreSubjectKeys.IsBrowser(row.Subject))
            .GroupBy(row => row.Subject, StringComparer.Ordinal)
            .Select(group => new {
                Subject = group.Key,
                Latest = group.Select(row => row.SentAt).OrderBy(stamp => stamp, StringComparer.Ordinal).Last(),
            })
            .Where(browser => string.CompareOrdinal(browser.Latest, threshold) < 0)
            .Select(browser => browser.Subject)];

        int browsers = _rows.RemoveAll(row => stale.Contains(row.Subject, StringComparer.Ordinal));

        return Task.FromResult(expired + browsers);
    }

    /// <summary>Puts a message in without the caps, as a database direct write would.</summary>
    public void Seed(string subject, string from, string body, string? toPageKey = null, string? topic = null, string? expiresAt = null) =>
        _rows.Add(new StoreMessageRow(
            ++_nextId,
            subject,
            toPageKey,
            topic,
            from,
            body,
            (int)uk.osric.asp.Sdk.StoreLifetimeKind.Person,
            expiresAt,
            StoreSubjectKeys.Stamp(_clock.GetUtcNow())));

    private StoreMessageRow? Find(IReadOnlyList<string> subjects, string? toPageKey, string? topic) {
        DateTimeOffset now = _clock.GetUtcNow();

        return _rows
            .Where(row => subjects.Contains(row.Subject, StringComparer.Ordinal)
                && (topic is null ? row.ToPageKey == toPageKey : row.Topic == topic)
                && IsLive(row, now))
            .OrderBy(row => row.Id)
            .FirstOrDefault();
    }

    private static bool IsLive(StoreMessageRow row, DateTimeOffset now) =>
        row.ExpiresAt is null || string.CompareOrdinal(row.ExpiresAt, StoreSubjectKeys.Stamp(now)) > 0;
}
