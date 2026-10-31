// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Users;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Web.Storage;

/// <summary>The SQLite-backed account store.</summary>
public sealed class SqliteUserStore(StorageDbContext database) : IUserStore {

    /// <inheritdoc/>
    public async Task<LocalUser?> FindAsync(string userName, CancellationToken cancellationToken = default) {
        return await database.Set<LocalUser>().AsNoTracking()
            .SingleOrDefaultAsync(user => user.UserName == userName, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> TryAddAsync(LocalUser user, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(user);

        if (await database.Set<LocalUser>().AnyAsync(existing => existing.UserName == user.UserName, cancellationToken)) {
            return false;
        }

        database.Add(user);

        try {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        } catch (DbUpdateException) {
            // Two CLI invocations can pass the existence check together; the primary key is the arbiter.
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateHashAsync(string userName, string passwordHash, CancellationToken cancellationToken = default) {
        int updated = await database.Set<LocalUser>()
            .Where(user => user.UserName == userName)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PasswordHash, passwordHash), cancellationToken);

        return updated > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> ChangePasswordAsync(string userName, string passwordHash, string securityStamp, CancellationToken cancellationToken = default) {
        int updated = await database.Set<LocalUser>()
            .Where(user => user.UserName == userName)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.PasswordHash, passwordHash)
                .SetProperty(user => user.SecurityStamp, securityStamp), cancellationToken);

        return updated > 0;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default) {
        return await database.Set<LocalUser>().AsNoTracking()
            .OrderBy(user => user.UserName)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(string userName, CancellationToken cancellationToken = default) {
        int removed = await database.Set<LocalUser>()
            .Where(user => user.UserName == userName)
            .ExecuteDeleteAsync(cancellationToken);

        return removed > 0;
    }
}
