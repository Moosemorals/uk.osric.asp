// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A page's own key/value store.</summary>
/// <remarks>
/// <para>
/// The handle is bound to the page that owns it, so a page can only reach its own keys. Every visitor
/// shares that state, and a write is last-write-wins: a page that increments a counter is racy under
/// concurrency.
/// </para>
/// <para>
/// A misuse is a page bug rather than a normal outcome, so it throws when the returned task is
/// awaited: a key that is too long, a value that cannot be serialised as JSON, a value over its cap,
/// a page over its total cap, a write on a <c>HEAD</c> request, or a write to a page that was deleted
/// while the render was running. The failure is detected before the row is written.
/// </para>
/// </remarks>
public interface IPageStorage {
    /// <summary>Reads a value and reports whether the key exists.</summary>
    /// <typeparam name="T">The type to deserialise the stored JSON as.</typeparam>
    /// <param name="key">The key; ordinal, 1 to 200 characters, no control characters.</param>
    /// <returns>
    /// Whether the key was found, and the value when it was. A missing key is not found with the
    /// default value. A stored JSON <c>null</c> is found with a null value, which is how a
    /// deliberately null value is told apart from an absent one. The test and the read are one
    /// operation, so they cannot race.
    /// </returns>
    /// <exception cref="System.Text.Json.JsonException">The stored value does not deserialise as <typeparamref name="T"/>.</exception>
    public Task<PageValue<T>> TryGetValueAsync<T>(string key);

    /// <summary>Stores a value, replacing any value already under the key.</summary>
    /// <typeparam name="T">The type to serialise.</typeparam>
    /// <param name="key">The key; ordinal, 1 to 200 characters, no control characters.</param>
    /// <param name="value">The value; a null is stored as JSON <c>null</c>.</param>
    /// <returns>A task that completes when the write is committed.</returns>
    /// <exception cref="ArgumentException">The key or the value breaks a cap.</exception>
    public Task SetAsync<T>(string key, T value);

    /// <summary>Removes a key.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>Whether the key existed. Deleting a missing key is not an error.</returns>
    public Task<bool> DeleteAsync(string key);

    /// <summary>Lists the page's keys.</summary>
    /// <returns>The keys, in ordinal order.</returns>
    public Task<IReadOnlyList<string>> KeysAsync();
}
