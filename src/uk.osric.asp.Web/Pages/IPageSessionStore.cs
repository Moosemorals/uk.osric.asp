// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The session operations the shell uses for one visitor's state.</summary>
/// <remarks>
/// Values cross this seam as JSON text, because presence and value are decided by one SQL read and
/// the store has no business knowing a page's types. The per-value cap is checked by the scope; the
/// per-session key count and byte total are enforced in the same statement as the write.
/// </remarks>
internal interface IPageSessionStore {

    /// <summary>Finds a session row.</summary>
    /// <param name="sessionId">The id from the visitor's cookie.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <see langword="null"/> when the id names no live session.</returns>
    Task<PageSessionEntry?> FindAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Mints a session row, evicting at the live-session cap when it has to.</summary>
    /// <param name="sessionId">The id to mint.</param>
    /// <param name="account">The normalised account name, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// Whether the row exists afterwards. A mint is refused only when the table is at its cap and
    /// nothing at all can be evicted.
    /// </returns>
    Task<bool> EnsureAsync(string sessionId, string? account, CancellationToken cancellationToken = default);

    /// <summary>Slides a session's idle timer.</summary>
    /// <param name="sessionId">The id that was touched.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task TouchAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a session and every value it holds.</summary>
    /// <param name="sessionId">The id to delete.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether a row was deleted.</returns>
    Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Reads one value.</summary>
    /// <param name="sessionId">The visitor's session.</param>
    /// <param name="pagePath">The canonical page key the value belongs to.</param>
    /// <param name="key">The key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored JSON, or <see langword="null"/> when the key is absent.</returns>
    Task<string?> GetValueAsync(string sessionId, string pagePath, string key, CancellationToken cancellationToken = default);

    /// <summary>Writes one value, applying every per-session cap in the same statement.</summary>
    /// <param name="sessionId">The visitor's session.</param>
    /// <param name="pagePath">The canonical page key the value belongs to.</param>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value as JSON.</param>
    /// <param name="caps">The caps this visitor's session obeys.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentException">A cap was hit, or the session no longer exists.</exception>
    Task SetValueAsync(
        string sessionId,
        string pagePath,
        string key,
        string value,
        PageSessionCaps caps,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one value.</summary>
    /// <param name="sessionId">The visitor's session.</param>
    /// <param name="pagePath">The canonical page key the value belongs to.</param>
    /// <param name="key">The key to remove.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the key existed.</returns>
    Task<bool> DeleteValueAsync(string sessionId, string pagePath, string key, CancellationToken cancellationToken = default);

    /// <summary>Lists one page's keys.</summary>
    /// <param name="sessionId">The visitor's session.</param>
    /// <param name="pagePath">The canonical page key whose keys are listed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The keys, in ordinal order.</returns>
    Task<IReadOnlyList<string>> ListKeysAsync(string sessionId, string pagePath, CancellationToken cancellationToken = default);

    /// <summary>Lists every page and key a session holds.</summary>
    /// <param name="sessionId">The visitor's session.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The pairs, ordered by page and then key.</returns>
    Task<IReadOnlyList<PageSessionKey>> ListAllKeysAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Removes expired sessions, and trims the table to its live-session cap when it is over.</summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>What the sweep removed, by kind.</returns>
    Task<PageSessionSweep> RemoveExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>What one session sweep removed, by kind.</summary>
/// <param name="Expired">Sessions past their idle window.</param>
/// <param name="AnonymousEvicted">Anonymous sessions removed to get back under the live-session cap.</param>
/// <param name="SignedInEvicted">Signed-in sessions removed to get back under the live-session cap.</param>
internal sealed record PageSessionSweep(int Expired, int AnonymousEvicted, int SignedInEvicted);
