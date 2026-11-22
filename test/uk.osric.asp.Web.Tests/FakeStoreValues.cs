// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IStoreValues"/> that applies the same rules as the real one.</summary>
/// <remarks>
/// The subject, lifetime, expiry and cap rules are enforced here too, so a value-store test proves the
/// behaviour the store promises without a database. The SQL is proven separately, against the real
/// store.
/// </remarks>
internal sealed class FakeStoreValues(PageStoresOptions? options = null, TestClock? clock = null) : IStoreValues {

    private readonly List<Row> _rows = [];
    private readonly PageStoresOptions _options = options ?? TestOptions.Stores;
    private readonly TestClock _clock = clock ?? new TestClock();

    /// <summary>How long a read takes, so a test can leave a page suspended in an <c>await</c>.</summary>
    public TimeSpan ReadDelay { get; set; }

    /// <summary>When set, a read waits for the gate, so a test can hold a page at a known point.</summary>
    public TaskCompletionSource? ReadGate { get; set; }

    /// <summary>Completes when a read has reached the point where it would wait for <see cref="ReadGate"/>.</summary>
    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Pages whose head is live; a Page-subject write to any other page is refused.</summary>
    public HashSet<string> LivePages { get; } = [];

    public int WriteCount { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>Every row, for a test that wants to prove what a subject holds.</summary>
    public IReadOnlyList<Row> Rows => _rows;

    /// <summary>The caps this fake was built with.</summary>
    public PageStoresOptions Options => _options;

    /// <summary>Reads a Page value, as the old page-state fake did.</summary>
    public async Task<string?> GetValueAsync(PagePath path, string key, CancellationToken cancellationToken = default) =>
        (await FindAsync([StoreSubjects.ForPage(path)], path.Key, key, cancellationToken: cancellationToken))?.Value;

    public async Task<StoreValueRow?> FindAsync(
        IReadOnlyList<string> subjects,
        string? pageKey,
        string key,
        StoreLifetimeKind? lifetime = null,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        _ = ReadStarted.TrySetResult();
        await WaitAsync(cancellationToken);

        Row? row = _rows
            .Where(candidate => subjects.Contains(candidate.Subject)
                && candidate.Key == key
                && (pageKey is null || candidate.PageKey == pageKey)
                && (lifetime is null || candidate.Lifetime == (int)lifetime)
                && candidate.IsLive(_clock.GetUtcNow()))
            .OrderBy(candidate => Rank(subjects, candidate.Subject))
            .ThenBy(candidate => candidate.PageKey, StringComparer.Ordinal)
            .FirstOrDefault();

        return row?.ToValue();
    }

    public async Task<StoreValueRow?> TakeAsync(
        IReadOnlyList<string> subjects,
        string? pageKey,
        string key,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await WaitAsync(cancellationToken);

        Row? row = _rows
            .Where(candidate => subjects.Contains(candidate.Subject)
                && candidate.Key == key
                && (pageKey is null || candidate.PageKey == pageKey)
                && candidate.Lifetime == (int)StoreLifetimeKind.NextRead
                && candidate.IsLive(_clock.GetUtcNow()))
            .OrderBy(candidate => candidate.PageKey, StringComparer.Ordinal)
            .FirstOrDefault();

        if (row is not null) {
            _ = _rows.Remove(row);
            WriteCount++;
        }

        return row?.ToValue();
    }

    public async Task<IReadOnlyList<StoreValueRow>> ListAsync(
        IReadOnlyList<string> subjects,
        string? pageKey,
        StoreLifetimeKind? lifetime,
        int limit,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await WaitAsync(cancellationToken);

        HashSet<string> seen = [with(StringComparer.Ordinal)];
        List<StoreValueRow> answer = [];
        foreach (Row row in _rows
            .Where(candidate => subjects.Contains(candidate.Subject)
                && (pageKey is null || candidate.PageKey == pageKey)
                && (lifetime is null || candidate.Lifetime == (int)lifetime)
                && candidate.IsLive(_clock.GetUtcNow()))
            .OrderBy(candidate => Rank(subjects, candidate.Subject))
            .ThenBy(candidate => candidate.PageKey, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)) {
            if (seen.Add(row.PageKey + "\n" + row.Key)) {
                answer.Add(row.ToValue());
                if (answer.Count >= limit) {
                    break;
                }
            }
        }

        return answer;
    }

    public async Task WriteAsync(StoreValueWrite write, PageStoreCaps caps, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        if (write.RequireLivePage && !LivePages.Contains(write.PageKey)) {
            throw new ArgumentException($"{write.PageKey} was deleted before its state could be written.");
        }

        List<Row> held = [.. _rows.Where(row => row.Subject == write.Subject)];
        bool existing = held.Any(row => row.PageKey == write.PageKey && row.Key == write.Key);
        if (!existing && held.Count >= caps.MaxKeys) {
            throw new ArgumentException($"The subject already holds {held.Count} keys and may hold {caps.MaxKeys}.");
        }

        long total = held
            .Where(row => !(row.PageKey == write.PageKey && row.Key == write.Key))
            .Sum(row => (long)Encoding.UTF8.GetByteCount(row.Key) + Encoding.UTF8.GetByteCount(row.Value));
        total += Encoding.UTF8.GetByteCount(write.Key) + Encoding.UTF8.GetByteCount(write.Value);
        if (total > caps.MaxTotalBytes) {
            throw new ArgumentException($"The subject would need {total} bytes of state and may use {caps.MaxTotalBytes}.");
        }

        Row? current = held.FirstOrDefault(row => row.PageKey == write.PageKey && row.Key == write.Key);
        if (current is not null) {
            current.Lifetime = write.Lifetime;
            current.ExpiresAt = write.ExpiresAt;
            current.Value = write.Value;
            current.UpdatedAt = StoreSubjects.Stamp(_clock.GetUtcNow());
        } else {
            _rows.Add(new Row {
                Subject = write.Subject,
                PageKey = write.PageKey,
                Key = write.Key,
                Lifetime = write.Lifetime,
                ExpiresAt = write.ExpiresAt,
                Value = write.Value,
                UpdatedAt = StoreSubjects.Stamp(_clock.GetUtcNow()),
            });
        }

        WriteCount++;
    }

    public async Task<bool> DeleteAsync(
        IReadOnlyList<string> subjects,
        string pageKey,
        string key,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await WaitAsync(cancellationToken);

        Row? row = _rows
            .Where(candidate => subjects.Contains(candidate.Subject)
                && candidate.PageKey == pageKey
                && candidate.Key == key)
            .OrderBy(candidate => Rank(subjects, candidate.Subject))
            .FirstOrDefault();

        return row is not null && _rows.Remove(row);
    }

    public async Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        DateTimeOffset now = _clock.GetUtcNow();
        int expired = _rows.RemoveAll(row => !row.IsLive(now));

        string threshold = StoreSubjects.Stamp(now - browserLifetime);
        string[] stale = [.. _rows
            .Where(row => StoreSubjects.IsBrowser(row.Subject))
            .GroupBy(row => row.Subject, StringComparer.Ordinal)
            .Select(group => new {
                Subject = group.Key,
                Latest = group.Select(row => row.UpdatedAt).OrderBy(stamp => stamp, StringComparer.Ordinal).Last(),
            })
            .Where(browser => string.CompareOrdinal(browser.Latest, threshold) < 0)
            .Select(browser => browser.Subject)];

        int browsers = _rows.RemoveAll(row => stale.Contains(row.Subject, StringComparer.Ordinal));
        return expired + browsers;
    }

    public async Task<IReadOnlyList<StoreValueRow>> GetPageValuesAsync(
        PagePath page,
        CancellationToken cancellationToken = default) {
        LastCancellationToken = cancellationToken;
        await Task.Yield();

        string subject = StoreSubjects.ForPage(page);
        return [.. _rows
            .Where(row => row.Subject == subject
                && row.Lifetime == (int)StoreLifetimeKind.Page
                && row.ExpiresAt is null)
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .Select(row => row.ToValue())];
    }

    public Task WritePageValueAsync(
        PagePath page,
        string key,
        string value,
        PageStoreCaps caps,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new StoreValueWrite(
                StoreSubjects.ForPage(page),
                page.Key,
                key,
                (int)StoreLifetimeKind.Page,
                ExpiresAt: null,
                value,
                RequireLivePage: true),
            caps,
            cancellationToken);

    /// <summary>Puts a Page value in without the caps, as a database direct write would.</summary>
    public void Seed(string pagePath, string key, string value) {
        LivePages.Add(pagePath);
        _rows.Add(new Row {
            Subject = StoreSubjects.PagePrefix + pagePath,
            PageKey = pagePath,
            Key = key,
            Lifetime = (int)StoreLifetimeKind.Page,
            Value = value,
            UpdatedAt = StoreSubjects.Stamp(_clock.GetUtcNow()),
        });
    }

    /// <summary>Puts a Visitor value in without the caps.</summary>
    public void SeedVisitor(string subject, string pagePath, string key, string value, StoreLifetimeKind kind) =>
        _rows.Add(new Row {
            Subject = subject,
            PageKey = pagePath,
            Key = key,
            Lifetime = (int)kind,
            Value = value,
            UpdatedAt = StoreSubjects.Stamp(_clock.GetUtcNow()),
        });

    /// <summary>Re-keys an anonymous session's person and one-shot entries into an account.</summary>
    public void Adopt(string sessionId, string account) {
        string from = StoreSubjects.ForSession(sessionId);
        string to = StoreSubjects.ForAccount(account);

        foreach (Row row in _rows.Where(row => row.Subject == from
            && row.Lifetime is (int)StoreLifetimeKind.Person or (int)StoreLifetimeKind.NextRead)) {
            row.Subject = to;
        }
    }

    /// <summary>Removes every row a subject holds.</summary>
    public void DeleteSubject(string subject) => _rows.RemoveAll(row => row.Subject == subject);

    /// <summary>Reads the raw JSON a subject holds for one page and key.</summary>
    public string? Raw(string subject, string pagePath, string key) =>
        _rows.FirstOrDefault(row => row.Subject == subject && row.PageKey == pagePath && row.Key == key)?.Value;

    /// <summary>How many rows a subject holds.</summary>
    public int Count(string subject) => _rows.Count(row => row.Subject == subject);

    private async Task WaitAsync(CancellationToken cancellationToken) {
        if (ReadGate is { } gate) {
            await gate.Task.WaitAsync(cancellationToken);
        }

        await Task.Delay(ReadDelay, cancellationToken);
    }

    private static int Rank(IReadOnlyList<string> subjects, string subject) {
        for (int index = 0; index < subjects.Count; index++) {
            if (string.Equals(subjects[index], subject, StringComparison.Ordinal)) {
                return index;
            }
        }

        return subjects.Count;
    }

    internal sealed class Row {
        public required string Subject { get; set; }
        public required string PageKey { get; init; }
        public required string Key { get; init; }
        public required int Lifetime { get; set; }
        public string? ExpiresAt { get; set; }
        public required string Value { get; set; }
        public required string UpdatedAt { get; set; }

        public bool IsLive(DateTimeOffset now) =>
            ExpiresAt is null || string.CompareOrdinal(ExpiresAt, StoreSubjects.Stamp(now)) > 0;

        public StoreValueRow ToValue() =>
            new(Subject, PageKey, Key, Lifetime, ExpiresAt, Value, UpdatedAt);
    }
}
