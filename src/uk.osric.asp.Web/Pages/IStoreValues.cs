// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>One row of <c>StoreValues</c>, as the store answers it.</summary>
/// <param name="Subject">The subject string the row belongs to.</param>
/// <param name="PageKey">The canonical key of the page that wrote it.</param>
/// <param name="Key">The entry's key.</param>
/// <param name="Lifetime">The stored <see cref="StoreLifetimeKind"/>.</param>
/// <param name="ExpiresAt">The fixed-width expiry, or <see langword="null"/>.</param>
/// <param name="Value">The value as JSON.</param>
/// <param name="UpdatedAt">When the row was last written, in fixed-width UTC.</param>
internal sealed record StoreValueRow(
    string Subject,
    string PageKey,
    string Key,
    int Lifetime,
    string? ExpiresAt,
    string Value,
    string UpdatedAt);

/// <summary>One value about to be written.</summary>
/// <param name="Subject">The subject the row is bound to.</param>
/// <param name="PageKey">The page that wrote it.</param>
/// <param name="Key">The entry's key.</param>
/// <param name="Lifetime">The resolved kind; never <c>Default</c>.</param>
/// <param name="ExpiresAt">The fixed-width expiry, or <see langword="null"/>.</param>
/// <param name="Value">The value as JSON.</param>
/// <param name="RequireLivePage">Whether the page's head has to be live, which a Page write requires.</param>
internal sealed record StoreValueWrite(
    string Subject,
    string PageKey,
    string Key,
    int Lifetime,
    string? ExpiresAt,
    string Value,
    bool RequireLivePage);

/// <summary>The value operations the shell uses, over <c>StoreValues</c>.</summary>
/// <remarks>
/// Values cross this seam as JSON text, because presence and value are decided by one SQL read and
/// the store has no business knowing a page's types. Every call owns its context, as the old stores
/// did: a render that was abandoned can outlive the request scope.
/// </remarks>
internal interface IStoreValues {

    /// <summary>Reads the entry a reader would see, or <see langword="null"/> when none is live.</summary>
    /// <param name="subjects">The reader's subjects, most ephemeral first.</param>
    /// <param name="pageKey">The page to read, or <see langword="null"/> for any page.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="lifetime">The lifetime to select, or <see langword="null"/> for every kind.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<StoreValueRow?> FindAsync(
        IReadOnlyList<string> subjects,
        string? pageKey,
        string key,
        StoreLifetimeKind? lifetime = null,
        CancellationToken cancellationToken = default);

    /// <summary>Takes the first one-shot a reader would see, removing it as it answers.</summary>
    /// <param name="subjects">The reader's subjects, most ephemeral first.</param>
    /// <param name="pageKey">The page to take from, or <see langword="null"/> for any page.</param>
    /// <param name="key">The one-shot's key.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    Task<StoreValueRow?> TakeAsync(
        IReadOnlyList<string> subjects, string? pageKey, string key, CancellationToken cancellationToken = default);

    /// <summary>Lists the live entries a reader would see, shadowed keys only once.</summary>
    /// <param name="subjects">The reader's subjects, most ephemeral first.</param>
    /// <param name="pageKey">The page to list, or <see langword="null"/> for every page.</param>
    /// <param name="lifetime">The lifetime to select, or <see langword="null"/> for every kind.</param>
    /// <param name="limit">The most entries to answer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoreValueRow>> ListAsync(
        IReadOnlyList<string> subjects,
        string? pageKey,
        StoreLifetimeKind? lifetime,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Writes one value under its subject's limits, in one statement.</summary>
    /// <param name="write">The row to write.</param>
    /// <param name="limits">The limits the subject obeys.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentException">A limit was hit, or the page is no longer live.</exception>
    Task WriteAsync(StoreValueWrite write, SubjectLimits limits, CancellationToken cancellationToken = default);

    /// <summary>Removes the entry a reader would see for this page and key.</summary>
    /// <param name="subjects">The reader's subjects, most ephemeral first.</param>
    /// <param name="pageKey">The page the entry belongs to.</param>
    /// <param name="key">The key to remove.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>Whether an entry was removed.</returns>
    Task<bool> DeleteAsync(
        IReadOnlyList<string> subjects, string pageKey, string key, CancellationToken cancellationToken = default);

    /// <summary>Removes expired values and browser subjects nothing has touched.</summary>
    /// <param name="browserLifetime">How long a browser subject survives after its last write.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>How many rows were removed.</returns>
    Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default);

    /// <summary>Reads the Page values an archive may carry: the page's own lifetime and no expiry.</summary>
    /// <param name="page">The page whose values are read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoreValueRow>> GetPageValuesAsync(PagePath page, CancellationToken cancellationToken = default);

    /// <summary>Writes one Page value back from an archive.</summary>
    /// <param name="page">The page the value belongs to.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value as JSON.</param>
    /// <param name="limits">The Page subject's limits.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentException">A limit was hit, or the page is no longer live.</exception>
    Task WritePageValueAsync(
        PagePath page, string key, string value, SubjectLimits limits, CancellationToken cancellationToken = default);
}
