// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>The session identity the shell owns: the row, its rotation and its sweep.</summary>
/// <remarks>
/// The values a visitor holds are no longer here: they live in <c>StoreValues</c> under the
/// <c>session:</c>, <c>browser:</c> or <c>account:</c> subject. This seam is the identity and the
/// cookie, and every delete it makes removes the session's values in the same transaction.
/// </remarks>
internal interface IPageSessionStore {

    /// <summary>Finds a session row.</summary>
    /// <param name="sessionId">The id from the visitor's cookie.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <see langword="null"/> when the id names no live session.</returns>
    Task<PageSessionEntry?> FindAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Mints a session row, evicting at the live-session limit when it has to.</summary>
    /// <param name="sessionId">The id to mint.</param>
    /// <param name="account">The normalised account name, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// Whether the row exists afterwards. A mint is refused only when the table is at its limit and
    /// nothing at all can be evicted.
    /// </returns>
    Task<bool> EnsureAsync(string sessionId, string? account, CancellationToken cancellationToken = default);

    /// <summary>Adopts an anonymous session's person data into an account and starts its session.</summary>
    /// <param name="previousSessionId">The session the visitor is leaving, or <see langword="null"/>.</param>
    /// <param name="account">The normalised account name being signed in to.</param>
    /// <param name="sessionId">The id to mint for the account.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether the account's session was minted.</returns>
    /// <remarks>
    /// One transaction: the <c>Person</c> and <c>NextRead</c> entries are re-keyed into the account,
    /// what is left under the old session is deleted, and the old row is replaced. A one-shot therefore
    /// survives sign-in unread. When <paramref name="previousSessionId"/> is <see langword="null"/> the
    /// call only mints.
    /// </remarks>
    Task<bool> AdoptAsync(
        string? previousSessionId,
        string account,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Slides a session's idle timer.</summary>
    /// <param name="sessionId">The id that was touched.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task TouchAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a session and every value it holds.</summary>
    /// <param name="sessionId">The id to delete.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether a row was deleted.</returns>
    Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Removes expired sessions, and trims the table to its live-session limit when it is over.</summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>What the sweep removed, by kind.</returns>
    Task<PageSessionSweep> RemoveExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>What one session sweep removed, by kind.</summary>
/// <param name="Expired">Sessions past their idle window.</param>
/// <param name="AnonymousEvicted">Anonymous sessions removed to get back under the live-session limit.</param>
/// <param name="SignedInEvicted">Signed-in sessions removed to get back under the live-session limit.</param>
internal sealed record PageSessionSweep(int Expired, int AnonymousEvicted, int SignedInEvicted);
