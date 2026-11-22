// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>One row of <c>StoreMessages</c>, as the store answers it.</summary>
/// <param name="Id">The row id, which is the FIFO order.</param>
/// <param name="Subject">The reader's subject the message belongs to.</param>
/// <param name="ToPageKey">The destination page's key, or <see langword="null"/> for a topic.</param>
/// <param name="Topic">The named queue, or <see langword="null"/> for a page address.</param>
/// <param name="FromPageKey">The canonical key of the source that posted it.</param>
/// <param name="Body">The text the poster wrote.</param>
/// <param name="Lifetime">The stored <see cref="StoreLifetimeKind"/>.</param>
/// <param name="ExpiresAt">The fixed-width expiry, or <see langword="null"/>.</param>
/// <param name="SentAt">When it was queued, in fixed-width UTC.</param>
internal sealed record StoreMessageRow(
    long Id,
    string Subject,
    string? ToPageKey,
    string? Topic,
    string FromPageKey,
    string Body,
    int Lifetime,
    string? ExpiresAt,
    string SentAt);

/// <summary>One message about to be written.</summary>
/// <param name="Subject">The reader's subject the row belongs to.</param>
/// <param name="ToPageKey">The destination page's key, or <see langword="null"/> for a topic.</param>
/// <param name="Topic">The named queue, or <see langword="null"/> for a page address.</param>
/// <param name="FromPageKey">The source that posted it.</param>
/// <param name="Body">The text the poster wrote.</param>
/// <param name="Lifetime">The resolved kind; never <c>Default</c>.</param>
/// <param name="ExpiresAt">The fixed-width expiry, or <see langword="null"/>.</param>
internal sealed record StoreMessageWrite(
    string Subject,
    string? ToPageKey,
    string? Topic,
    string FromPageKey,
    string Body,
    int Lifetime,
    string? ExpiresAt);

/// <summary>The message operations the shell uses, over <c>StoreMessages</c>.</summary>
/// <remarks>
/// A take is one statement that removes the row it answers, so nothing can take a message twice; a
/// peek is the same statement with a select, so it answers what a take would answer and removes
/// nothing. Every call owns its context, as the value store does: a render that was abandoned can
/// outlive the request scope.
/// </remarks>
internal interface IStoreMessages {

    /// <summary>Queues one message, under its subject's caps, in one statement.</summary>
    /// <param name="write">The row to write.</param>
    /// <param name="caps">The caps the subject obeys.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the row was written: <see langword="false"/> means a cap refused it.</returns>
    Task<bool> PostAsync(StoreMessageWrite write, PageMessageOptions caps, CancellationToken cancellationToken = default);

    /// <summary>Takes the oldest message the reader would see for one address, removing it.</summary>
    /// <param name="subjects">The reader's subjects, most ephemeral first.</param>
    /// <param name="toPageKey">The destination page's key, or <see langword="null"/> for a topic.</param>
    /// <param name="topic">The named queue, or <see langword="null"/> for a page address.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The row taken, or <see langword="null"/> when none was waiting.</returns>
    Task<StoreMessageRow?> TakeAsync(
        IReadOnlyList<string> subjects,
        string? toPageKey,
        string? topic,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the oldest message a take would remove, changing nothing.</summary>
    /// <param name="subjects">The reader's subjects, most ephemeral first.</param>
    /// <param name="toPageKey">The destination page's key, or <see langword="null"/> for a topic.</param>
    /// <param name="topic">The named queue, or <see langword="null"/> for a page address.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The row a take would take, or <see langword="null"/> when none is waiting.</returns>
    Task<StoreMessageRow?> PeekAsync(
        IReadOnlyList<string> subjects,
        string? toPageKey,
        string? topic,
        CancellationToken cancellationToken = default);

    /// <summary>Removes expired messages and browser messages whose subject has gone quiet.</summary>
    /// <param name="browserLifetime">How long a browser subject survives after its last write.</param>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>How many rows were removed.</returns>
    Task<int> RemoveExpiredAsync(TimeSpan browserLifetime, CancellationToken cancellationToken = default);
}
