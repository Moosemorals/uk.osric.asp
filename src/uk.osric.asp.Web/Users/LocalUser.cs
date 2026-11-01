// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

/// <summary>A local account that may sign in to the editor.</summary>
/// <remarks>
/// The user name is already normalised by <see cref="UserName.Normalize"/>; the password is stored
/// only as a hash produced by <see cref="IPasswordService"/>. The security stamp is rotated when the
/// password changes, so existing long-term cookies stop validating.
/// </remarks>
internal sealed class LocalUser {

    /// <summary>The normalised user name, which is the primary key.</summary>
    public required string UserName { get; init; }

    /// <summary>The password hash.</summary>
    public required string PasswordHash { get; init; }

    /// <summary>The revocation stamp carried in an authentication cookie.</summary>
    public required string SecurityStamp { get; init; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
