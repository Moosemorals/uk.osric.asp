// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

/// <summary>An authentication ticket held on the server, keyed by the random value in the cookie.</summary>
internal sealed class AuthTicket {

    /// <summary>The random, unguessable key the cookie carries; 128 bits, base64url-encoded.</summary>
    public required string Key { get; init; }

    /// <summary>The account the ticket belongs to, so a sign-in never inherits another one's key.</summary>
    public required string UserName { get; init; }

    /// <summary>The framework's own data-protected ticket, exactly as it would have gone in the cookie.</summary>
    public required string ProtectedTicket { get; init; }

    /// <summary>When the ticket stops being valid, so a sweep can drop rows nobody returns to.</summary>
    /// <remarks>UTC, because SQLite can only compare and order the <see cref="DateTime"/> mapping.</remarks>
    public DateTime ExpiresAt { get; init; }
}
