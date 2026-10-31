// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

/// <summary>Stores the local accounts the CLI manages.</summary>
/// <remarks>Every user name passed to this store is already normalised by <see cref="UserName.Normalize"/>.</remarks>
public interface IUserStore {

    /// <summary>Finds an account by its normalised name.</summary>
    /// <param name="userName">The normalised name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<LocalUser?> FindAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>Adds an account unless the name is already taken.</summary>
    /// <param name="user">The account to add.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the account was added.</returns>
    Task<bool> TryAddAsync(LocalUser user, CancellationToken cancellationToken = default);

    /// <summary>Replaces the hash without touching the security stamp.</summary>
    /// <param name="userName">The normalised name.</param>
    /// <param name="passwordHash">The new hash.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the account existed.</returns>
    Task<bool> UpdateHashAsync(string userName, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>Replaces the hash and the security stamp, revoking existing cookies.</summary>
    /// <param name="userName">The normalised name.</param>
    /// <param name="passwordHash">The new hash.</param>
    /// <param name="securityStamp">The new stamp.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the account existed.</returns>
    Task<bool> ChangePasswordAsync(string userName, string passwordHash, string securityStamp, CancellationToken cancellationToken = default);

    /// <summary>Lists accounts in name order.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes an account.</summary>
    /// <param name="userName">The normalised name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the account existed.</returns>
    Task<bool> RemoveAsync(string userName, CancellationToken cancellationToken = default);
}
