// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Users;

/// <summary>An in-memory account store for endpoint and command tests.</summary>
internal sealed class FakeUserStore : IUserStore {

    public Dictionary<string, LocalUser> Users { get; } = new(StringComparer.Ordinal);

    public int HashUpdates { get; private set; }

    public Task<LocalUser?> FindAsync(string userName, CancellationToken cancellationToken = default) {
        return Task.FromResult(Users.GetValueOrDefault(userName));
    }

    public Task<bool> TryAddAsync(LocalUser user, CancellationToken cancellationToken = default) {
        if (Users.ContainsKey(user.UserName)) {
            return Task.FromResult(false);
        }

        Users[user.UserName] = user;
        return Task.FromResult(true);
    }

    public Task<bool> UpdateHashAsync(string userName, string passwordHash, CancellationToken cancellationToken = default) {
        if (!Users.TryGetValue(userName, out LocalUser? user)) {
            return Task.FromResult(false);
        }

        HashUpdates++;
        Users[userName] = Copy(user, passwordHash, user.SecurityStamp);
        return Task.FromResult(true);
    }

    public Task<bool> ChangePasswordAsync(string userName, string passwordHash, string securityStamp, CancellationToken cancellationToken = default) {
        if (!Users.TryGetValue(userName, out LocalUser? user)) {
            return Task.FromResult(false);
        }

        Users[userName] = Copy(user, passwordHash, securityStamp);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default) {
        IReadOnlyList<LocalUser> users = [.. Users.Values.OrderBy(user => user.UserName, StringComparer.Ordinal)];
        return Task.FromResult(users);
    }

    public Task<bool> RemoveAsync(string userName, CancellationToken cancellationToken = default) {
        return Task.FromResult(Users.Remove(userName));
    }

    private static LocalUser Copy(LocalUser user, string passwordHash, string securityStamp) {
        return new LocalUser {
            UserName = user.UserName,
            PasswordHash = passwordHash,
            SecurityStamp = securityStamp,
            CreatedAt = user.CreatedAt,
        };
    }
}
