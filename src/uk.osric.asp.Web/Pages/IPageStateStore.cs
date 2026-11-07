// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;

/// <summary>The path-parameterised storage operations the shell uses for one page's state.</summary>
/// <remarks>
/// Values cross this seam as JSON text, because presence and value are decided by one SQL read and
/// the store has no business knowing a page's types. Caps that need no other row are enforced here;
/// the per-page caps are enforced in the same statement as the write.
/// </remarks>
internal interface IPageStateStore {

    /// <summary>Reads one value.</summary>
    /// <param name="path">The page whose state is read.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored JSON, or <see langword="null"/> when the key is absent.</returns>
    Task<string?> GetValueAsync(PagePath path, string key, CancellationToken cancellationToken = default);

    /// <summary>Writes one value, applying every per-page cap in the same statement.</summary>
    /// <param name="path">The page whose state is written.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value as JSON.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentException">A cap was hit, or the page is no longer live.</exception>
    Task SetValueAsync(PagePath path, string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Removes one key.</summary>
    /// <param name="path">The page whose state is written.</param>
    /// <param name="key">The key to remove.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the key existed.</returns>
    Task<bool> DeleteValueAsync(PagePath path, string key, CancellationToken cancellationToken = default);

    /// <summary>Lists a page's keys.</summary>
    /// <param name="path">The page whose keys are listed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The keys, in ordinal order.</returns>
    Task<IReadOnlyList<string>> ListKeysAsync(PagePath path, CancellationToken cancellationToken = default);

    /// <summary>Reads every key and value a page holds.</summary>
    /// <param name="path">The page whose state is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The values, in ordinal key order; empty when the page has no state.</returns>
    Task<IReadOnlyList<PageStateValue>> GetValuesAsync(PagePath path, CancellationToken cancellationToken = default);
}
