// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using uk.osric.asp.Web.Users;

/// <summary>Answers whether a job's borrowed credential is still one a request would accept.</summary>
/// <remarks>
/// It makes the two checks the cookie and the view make on every request: the account exists with the
/// stamp the credential captured, and the handle still resolves to it. The ticket's expiry is compared
/// with the clock rather than left to the hourly sweep, so a job's authority ends when the browser's
/// sign-in does.
/// </remarks>
internal sealed class PageJobAuthorityVerifier(
    IServiceScopeFactory scopes,
    AuthTicketStore tickets,
    ViewTokenStore runs,
    TimeProvider clock) {

    /// <summary>Whether the credential would still authenticate a request.</summary>
    /// <param name="authority">The credential a job borrowed.</param>
    /// <param name="cancellationToken">Cancels the account read.</param>
    /// <returns><see langword="true"/> while the account and the handle behind it are still live.</returns>
    public async Task<bool> IsLiveAsync(PageJobAuthority authority, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(authority);

        LocalUser? user;
        using (IServiceScope scope = scopes.CreateScope()) {
            IUserStore users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            user = await users.FindAsync(authority.Owner.Name, cancellationToken);
        }

        if (user is null || !string.Equals(user.SecurityStamp, authority.SecurityStamp, StringComparison.Ordinal)) {
            return false;
        }

        if (authority.RunId is { } runId) {
            ViewToken? run = runs.ForRun(runId);

            return run is not null && string.Equals(run.Owner, authority.Owner.Name, StringComparison.Ordinal);
        }

        if (authority.TicketKey is { } key) {
            AuthTicket? ticket = tickets.Find(key);

            return ticket is not null
                && string.Equals(ticket.UserName, authority.Owner.Name, StringComparison.Ordinal)
                && ticket.ExpiresAt > clock.GetUtcNow().UtcDateTime;
        }

        return false;
    }
}
