// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One visitor's key/value store, bounded and cleared when they sign out.</summary>
/// <remarks>
/// <para>
/// The handle is bound to the page that owns it, so a page writes, deletes and lists its own keys
/// and cannot name another page's. Reading is deliberately open: a page may read another page's key
/// by that page's path, which is what lets a page show what another page left for it. A value is
/// addressed by <c>(page, key)</c>; a bare key is not enough.
/// </para>
/// <para>
/// Every visitor gets a session of their own. It is minted when a page first writes and travels in
/// an <c>HttpOnly</c> cookie the shell owns; a page that never writes costs a visitor nothing and
/// gets no cookie. The session is cleared when the visitor signs out, and a session belonging to an
/// account is only honoured for that account.
/// </para>
/// <para>
/// Values are JSON. Keys are ordinal and case-sensitive, 1 to the configured maximum characters,
/// with no control characters. A write validates before it returns, and a read fails when its task
/// is awaited: a key that is too long, a value that cannot be serialised as JSON, a value over its
/// cap, a session over its key count or byte total, a write on a <c>HEAD</c> request, or a write to
/// a session the shell has already discarded. A layout is offered the read and not the write.
/// </para>
/// <para>
/// The cap is per visitor, not per page: one page's keys can use the room another would have had.
/// The idle lifetime is thirty minutes anonymously and eight hours when signed in.
/// </para>
/// </remarks>
public interface IPageSession {
    /// <summary>Reads one of this page's values and reports whether the key exists.</summary>
    /// <typeparam name="T">The type to deserialise the stored JSON as.</typeparam>
    /// <param name="key">The key; ordinal, 1 to the configured maximum characters, no control
    /// characters.</param>
    /// <returns>
    /// Whether the key was found, and the value when it was. A missing key is not found with the
    /// default value. A stored JSON <c>null</c> is found with a null value, which is how a
    /// deliberately null value is told apart from an absent one. The test and the read are one
    /// operation, so they cannot race.
    /// </returns>
    /// <exception cref="System.Text.Json.JsonException">The stored value does not deserialise as <typeparamref name="T"/>.</exception>
    public Task<PageValue<T>> TryGetValueAsync<T>(string key);

    /// <summary>Reads a value another page wrote.</summary>
    /// <typeparam name="T">The type to deserialise the stored JSON as.</typeparam>
    /// <param name="pagePath">The other page's path. It is normalised before use, so
    /// <c>../</c>, a trailing slash and a different case all name the same page.</param>
    /// <param name="key">The key that page wrote.</param>
    /// <returns>Whether the key was found, and the value when it was.</returns>
    /// <exception cref="System.ArgumentException">The path cannot name a page.</exception>
    /// <exception cref="System.Text.Json.JsonException">The stored value does not deserialise as <typeparamref name="T"/>.</exception>
    public Task<PageValue<T>> TryGetValueAsync<T>(string pagePath, string key);

    /// <summary>Stores one of this page's values, replacing any value already under the key.</summary>
    /// <typeparam name="T">The type to serialise.</typeparam>
    /// <param name="key">The key; ordinal, 1 to the configured maximum characters, no control
    /// characters.</param>
    /// <param name="value">The value; a null is stored as JSON <c>null</c>.</param>
    /// <returns>A task that completes when the write is committed.</returns>
    /// <exception cref="System.ArgumentException">The key or the value breaks a cap.</exception>
    /// <exception cref="System.InvalidOperationException">A layout or a <c>HEAD</c> render tried to write.</exception>
    public Task SetAsync<T>(string key, T value);

    /// <summary>Removes one of this page's keys.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>Whether the key existed. Deleting a missing key is not an error.</returns>
    /// <exception cref="System.InvalidOperationException">A layout or a <c>HEAD</c> render tried to write.</exception>
    public Task<bool> DeleteAsync(string key);

    /// <summary>Lists this page's keys.</summary>
    /// <returns>The keys, in ordinal order.</returns>
    public Task<IReadOnlyList<string>> KeysAsync();

    /// <summary>Lists every key this visitor's session holds, from every page.</summary>
    /// <returns>The page and key pairs, ordered by page and then key.</returns>
    /// <remarks>
    /// A bare key cannot be read, so the pair is what is returned. This is bounded by the
    /// visitor's own cap and cannot reach another visitor.
    /// </remarks>
    public Task<IReadOnlyList<PageSessionKey>> AllKeysAsync();
}
