// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One secret in <c>StoreSecrets</c>, as the store answers it.</summary>
/// <param name="Subject">The subject string the row belongs to; always a page's.</param>
/// <param name="PageKey">The canonical key of the page that owns it.</param>
/// <param name="Name">The secret's name.</param>
/// <param name="Value">The text.</param>
/// <param name="UpdatedAt">When the row was last written, in fixed-width UTC.</param>
internal sealed record StoreSecretRow(
    string Subject,
    string PageKey,
    string Name,
    string Value,
    string UpdatedAt);

/// <summary>One secret about to be written.</summary>
/// <param name="Subject">The subject the row is bound to.</param>
/// <param name="PageKey">The page that owns it.</param>
/// <param name="Name">The secret's name.</param>
/// <param name="Value">The text.</param>
/// <param name="RequireLivePage">Whether the page's head has to be live, which a page write requires.</param>
internal sealed record StoreSecretWrite(
    string Subject,
    string PageKey,
    string Name,
    string Value,
    bool RequireLivePage);

/// <summary>The secret operations the shell uses, over <c>StoreSecrets</c>.</summary>
/// <remarks>
/// Only a page's own key reaches this seam: there is no reader-subject list and no cross-page read,
/// because a secret is authority rather than data. Every call owns its context, as the other stores do,
/// so a render that was abandoned can outlive the request scope.
/// </remarks>
internal interface IStoreSecrets {

    /// <summary>Reads the secret a page holds under a name.</summary>
    /// <param name="subject">The owner's subject string.</param>
    /// <param name="name">The secret's name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <see langword="null"/> when the page holds no secret of that name.</returns>
    Task<StoreSecretRow?> FindAsync(string subject, string name, CancellationToken cancellationToken = default);

    /// <summary>Lists a page's secret names, never a value.</summary>
    /// <param name="subject">The owner's subject string.</param>
    /// <param name="limit">The most names to answer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoreSecretRow>> ListAsync(
        string subject, int limit, CancellationToken cancellationToken = default);

    /// <summary>Writes one secret under its subject's limits, in one statement.</summary>
    /// <param name="write">The row to write.</param>
    /// <param name="limits">The limits the subject obeys.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="ArgumentException">A limit was hit, or the page is no longer live.</exception>
    Task WriteAsync(StoreSecretWrite write, PageSecretOptions limits, CancellationToken cancellationToken = default);

    /// <summary>Removes one of a page's secrets.</summary>
    /// <param name="subject">The owner's subject string.</param>
    /// <param name="name">The secret's name.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>Whether a secret was removed.</returns>
    Task<bool> DeleteAsync(string subject, string name, CancellationToken cancellationToken = default);
}
