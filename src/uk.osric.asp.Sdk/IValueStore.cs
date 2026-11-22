// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A subject's key/value store: JSON, an ordinal key, and last write wins.</summary>
/// <remarks>
/// <para>
/// The handle is bound to the subject and to the page it belongs to, so a write always names that
/// page's key; there is no argument that names another writer. Reads are open across page keys,
/// because a page showing what another page left for it is the point.
/// </para>
/// <para>
/// A write validates before it returns and a read fails when its task is awaited: a key that breaks
/// the length rule, a value that cannot be serialised, a value or a subject over its cap, a lifetime
/// this subject cannot carry, or a write during a <c>HEAD</c>, an abandoned render or a passed
/// deadline. A cap is checked in the same statement as the write, so a concurrent writer either fits
/// or is refused.
/// </para>
/// </remarks>
public interface IValueStore {

    /// <summary>Reads one of the bound page's values and reports whether the key exists.</summary>
    /// <typeparam name="T">The type to deserialise the stored JSON as.</typeparam>
    /// <param name="key">The key; ordinal, 1 to the configured maximum, no control characters.</param>
    /// <returns>
    /// Whether the key was found, the value when it was, the lifetime it follows and the page that
    /// wrote it. A missing or expired key is not found with the default value. A stored JSON
    /// <c>null</c> is found with a null value.
    /// </returns>
    /// <exception cref="System.Text.Json.JsonException">The stored value does not deserialise as <typeparamref name="T"/>.</exception>
    public Task<PageValue<T>> TryGetValueAsync<T>(string key);

    /// <summary>Reads the value another page wrote, or this page's own when <paramref name="pageKey"/> is its key.</summary>
    /// <typeparam name="T">The type to deserialise the stored JSON as.</typeparam>
    /// <param name="pageKey">The other page's key. It is normalised before use.</param>
    /// <param name="key">The key that page wrote.</param>
    /// <returns>Whether the key was found, and the value, lifetime and page when it was.</returns>
    /// <exception cref="System.ArgumentException">The page key cannot name a page.</exception>
    /// <exception cref="System.Text.Json.JsonException">The stored value does not deserialise as <typeparamref name="T"/>.</exception>
    public Task<PageValue<T>> TryGetValueAsync<T>(string pageKey, string key);

    /// <summary>Takes a one-shot value, removing it as it answers.</summary>
    /// <typeparam name="T">The type to deserialise the stored JSON as.</typeparam>
    /// <param name="key">The one-shot's key.</param>
    /// <returns>
    /// The value when one was waiting, or not found. On a Visitor subject the take is the first
    /// <see cref="StoreLifetime.NextRead"/> entry for that key across the reader's pages; on a Page
    /// subject it is the bound page's own. A take that returns not found consumed nothing.
    /// </returns>
    /// <exception cref="System.Text.Json.JsonException">The stored value does not deserialise as <typeparamref name="T"/>.</exception>
    public Task<PageValue<T>> TryTakeAsync<T>(string key);

    /// <summary>Stores one of the bound page's values, replacing whatever the key held.</summary>
    /// <typeparam name="T">The type to serialise.</typeparam>
    /// <param name="key">The key; ordinal, 1 to the configured maximum, no control characters.</param>
    /// <param name="value">The value; a null is stored as JSON <c>null</c>.</param>
    /// <param name="lifetime">
    /// The entry's lifetime, or <see langword="null"/> for the subject's own: <see cref="StoreLifetime.Page"/>
    /// on a Page subject, <see cref="StoreLifetime.Person"/> on a Visitor. A lifetime the subject cannot
    /// carry is a page bug and throws.
    /// </param>
    /// <returns>A task that completes when the write is committed.</returns>
    /// <exception cref="System.ArgumentException">The key, the value or the lifetime breaks a rule or a cap.</exception>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public Task SetAsync<T>(string key, T value, StoreLifetime? lifetime = null);

    /// <summary>Removes the entry a read would answer for this page's key.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>Whether an entry was removed. Deleting a missing key is not an error.</returns>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public Task<bool> DeleteAsync(string key);

    /// <summary>Lists the bound page's entries without consuming anything.</summary>
    /// <param name="filter">A lifetime to select and a limit, or <see langword="null"/> for every entry.</param>
    /// <returns>The entries, ordered by page and then key, the shadowing entry first for a Visitor.</returns>
    public Task<IReadOnlyList<StoreEntry>> KeysAsync(StoreFilter? filter = null);

    /// <summary>Lists every entry the subject holds, across pages, without consuming anything.</summary>
    /// <param name="filter">A lifetime to select and a limit, or <see langword="null"/> for every entry.</param>
    /// <returns>The entries, ordered by page and then key, the shadowing entry first for a Visitor.</returns>
    public Task<IReadOnlyList<StoreEntry>> AllKeysAsync(StoreFilter? filter = null);
}
