// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>An in-memory <see cref="IStoreObjects"/> for the capability tests.</summary>
internal sealed class FakeStoreObjects : IStoreObjects {

    private readonly List<FakeObjectEntry> _rows = [];

    /// <summary>Every row, for a test that wants to prove what a subject holds.</summary>
    public IReadOnlyList<FakeObjectEntry> Rows => _rows;

    /// <summary>How many times a write reached the store.</summary>
    public int WriteCount { get; private set; }

    /// <summary>Adds a row directly, as a previous render or page would have.</summary>
    public FakeObjectEntry Add(
        string subject,
        byte[] content,
        string contentType = "application/octet-stream",
        StoreLifetimeKind lifetime = StoreLifetimeKind.Page,
        DateTimeOffset? expiresAt = null) {
        FakeObjectEntry entry = new(
            subject,
            PageObjectHash.Of(content),
            content.LongLength,
            contentType,
            lifetime,
            expiresAt,
            content);

        _rows.RemoveAll(row => row.Subject == subject && row.Sha == entry.Sha);
        _rows.Add(entry);

        return entry;
    }

    /// <inheritdoc/>
    public Task<PageObjectStream?> OpenPageAsync(string sha, CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        FakeObjectEntry? row = Live().FirstOrDefault(item =>
            item.Sha == sha && item.Subject.StartsWith(StoreSubjectKeys.PagePrefix, StringComparison.Ordinal));

        return Task.FromResult<PageObjectStream?>(row is null ? null : Reader(row));
    }

    /// <inheritdoc/>
    public Task<PageObjectStream?> OpenAsync(
        IReadOnlyList<string> subjects,
        string sha,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        FakeObjectEntry? row = First(subjects, sha);

        return Task.FromResult<PageObjectStream?>(row is null ? null : Reader(row));
    }

    /// <inheritdoc/>
    public Task<IServedObject?> OpenServedAsync(
        string sha,
        IReadOnlyList<string> subjects,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        FakeObjectEntry? row = Live()
            .Where(item => item.Sha == sha)
            .OrderBy(item => item.Subject.StartsWith(StoreSubjectKeys.PagePrefix, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(item => Rank(subjects, item.Subject))
            .FirstOrDefault();

        return Task.FromResult<IServedObject?>(row is null ? null : Reader(row));
    }

    /// <inheritdoc/>
    public Task WriteAsync(
        StoreObjectWrite write,
        PageObjectOptions limits,
        CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        _ = limits;
        WriteCount++;
        _ = Add(
            write.Subject,
            write.Content,
            write.ContentType,
            (StoreLifetimeKind)write.Lifetime,
            write.ExpiresAt is null ? null : DateTimeOffset.Parse(write.ExpiresAt, System.Globalization.CultureInfo.InvariantCulture));

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default) {
        _ = cancellationToken;
        _ = browserLifetime;
        int now = _rows.RemoveAll(row => row.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow);

        return Task.FromResult(now);
    }

    private IEnumerable<FakeObjectEntry> Live() =>
        _rows.Where(row => row.ExpiresAt is not { } expiry || expiry > DateTimeOffset.UtcNow);

    private FakeObjectEntry? First(IReadOnlyList<string> subjects, string sha) =>
        Live()
            .Where(row => row.Sha == sha && subjects.Contains(row.Subject, StringComparer.Ordinal))
            .OrderBy(row => Rank(subjects, row.Subject))
            .FirstOrDefault();

    private static int Rank(IReadOnlyList<string> subjects, string subject) {
        for (int index = 0; index < subjects.Count; index++) {
            if (string.Equals(subjects[index], subject, StringComparison.Ordinal)) {
                return index;
            }
        }

        return subjects.Count;
    }

    private static FakeObjectStream Reader(FakeObjectEntry row) => new(row);
}

/// <summary>One row in the fake object store.</summary>
internal sealed record FakeObjectEntry(
    string Subject,
    string Sha,
    long Length,
    string ContentType,
    StoreLifetimeKind Lifetime,
    DateTimeOffset? ExpiresAt,
    byte[] Content);

/// <summary>A <see cref="PageObjectStream"/> over a byte array, for the fake store.</summary>
internal sealed class FakeObjectStream(FakeObjectEntry row) : PageObjectStream, IServedObject {

    /// <inheritdoc/>
    public override string Sha => row.Sha;

    /// <inheritdoc/>
    public override string ContentType => row.ContentType;

    /// <inheritdoc/>
    public override long Length => row.Length;

    /// <inheritdoc/>
    public string Subject => row.Subject;

    /// <inheritdoc/>
    public bool Public => row.Subject.StartsWith(StoreSubjectKeys.PagePrefix, StringComparison.Ordinal);

    /// <summary>Whether the stream has been closed, for a test that proves the handle closes its readers.</summary>
    public bool Closed { get; private set; }

    /// <inheritdoc/>
    public ValueTask<int> ReadAtAsync(Memory<byte> buffer, long offset, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (offset >= row.Content.LongLength || buffer.IsEmpty) {
            return ValueTask.FromResult(0);
        }

        int count = (int)Math.Min(buffer.Length, row.Content.LongLength - offset);
        row.Content.AsSpan((int)offset, count).CopyTo(buffer.Span);

        return ValueTask.FromResult(count);
    }

    /// <inheritdoc/>
    protected override ValueTask<int> ReadCoreAsync(
        Memory<byte> buffer,
        long offset,
        CancellationToken cancellationToken) =>
        ReadAtAsync(buffer, offset, cancellationToken);

    /// <inheritdoc/>
    protected override void DisposeCore() => Closed = true;

    /// <inheritdoc/>
    protected override ValueTask DisposeCoreAsync() {
        Closed = true;
        return ValueTask.CompletedTask;
    }
}
