// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>The credential a job borrowed from the render that started it.</summary>
/// <remarks>
/// <para>
/// It is the owner, the account's revocation stamp and one handle: the sign-in's ticket key, or the id
/// of the run whose view presented the request. The endpoint captures it once per render and the job
/// keeps it for its life; the worker never sees it, because the capability is constructed around it
/// exactly as <see cref="PageWritesScope"/> is constructed around the visitor.
/// </para>
/// <para>
/// It is also the answer the writer asks for at each commit: when the credential is no longer live,
/// the commit stores nothing and every operation answers <see cref="PageWriteOutcome.Revoked"/>.
/// </para>
/// </remarks>
internal sealed class PageJobAuthority(
    PageUser owner,
    string? securityStamp,
    string? ticketKey,
    string? runId,
    PageJobAuthorityVerifier verifier,
    ILogger logger) : IPageWriteCredential {

    /// <summary>The account the job runs for.</summary>
    public PageUser Owner { get; } = owner;

    /// <summary>The account's revocation stamp when the credential was captured.</summary>
    public string? SecurityStamp { get; } = securityStamp;

    /// <summary>The sign-in's ticket key, or <see langword="null"/> when the credential is a view.</summary>
    public string? TicketKey { get; } = ticketKey;

    /// <summary>The run's id, or <see langword="null"/> when the credential is a sign-in.</summary>
    public string? RunId { get; } = runId;

    /// <inheritdoc/>
    public async ValueTask<bool> IsLiveAsync(CancellationToken cancellationToken) {
        if (await verifier.IsLiveAsync(this, cancellationToken)) {
            return true;
        }

        // The runner's log scope carries the job's id, so the line names what lost its authority.
        logger.LogWarning(
            "The credential {Owner} started this work with is no longer live; nothing was written",
            Owner.Name);

        return false;
    }
}
