// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Authentication;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using uk.osric.asp.Web.Storage;

/// <summary>Reads and writes <see cref="AuthTicket"/> rows, opening a scope per call.</summary>
/// <remarks>
/// Synchronous because <see cref="Microsoft.AspNetCore.Authentication.ISecureDataFormat{TData}"/> is,
/// and the work is one indexed statement against a local SQLite file.
/// </remarks>
internal sealed class AuthTicketStore(IServiceScopeFactory scopes) {

    /// <summary>Finds the ticket stored under a key.</summary>
    /// <param name="key">The cookie's key.</param>
    /// <returns>The row, or <see langword="null"/> when the key is unknown.</returns>
    public AuthTicket? Find(string key) {
        ArgumentNullException.ThrowIfNull(key);

        return WithDatabase(database => database.Set<AuthTicket>().AsNoTracking()
            .SingleOrDefault(row => row.Key == key));
    }

    /// <summary>Adds a ticket under a key that is not yet stored.</summary>
    /// <param name="ticket">The row to add.</param>
    public void Add(AuthTicket ticket) {
        ArgumentNullException.ThrowIfNull(ticket);

        _ = WithDatabase(database => {
            database.Add(ticket);
            return database.SaveChanges();
        });
    }

    /// <summary>Replaces the ticket stored under a key, when the key still belongs to the account.</summary>
    /// <param name="key">The cookie's key.</param>
    /// <param name="userName">The account the new ticket belongs to.</param>
    /// <param name="protectedTicket">The framework's newly protected ticket.</param>
    /// <param name="expiresAt">The ticket's new expiry, in UTC.</param>
    /// <returns>Whether a row was updated.</returns>
    public bool Update(string key, string userName, string protectedTicket, DateTime expiresAt) {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(protectedTicket);

        return WithDatabase(database => database.Set<AuthTicket>()
            .Where(row => row.Key == key && row.UserName == userName)
            .ExecuteUpdate(setters => setters
                .SetProperty(row => row.ProtectedTicket, protectedTicket)
                .SetProperty(row => row.ExpiresAt, expiresAt))) > 0;
    }

    /// <summary>Deletes the ticket stored under a key.</summary>
    /// <param name="key">The cookie's key.</param>
    /// <returns>Whether a row was deleted.</returns>
    public bool Remove(string key) {
        ArgumentNullException.ThrowIfNull(key);

        return WithDatabase(database => database.Set<AuthTicket>().Where(row => row.Key == key).ExecuteDelete()) > 0;
    }

    /// <summary>Deletes rows whose ticket has expired, which no request will ever remove again.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The number of rows deleted.</returns>
    public int RemoveExpired(DateTime now) =>
        WithDatabase(database => database.Set<AuthTicket>().Where(row => row.ExpiresAt < now).ExecuteDelete());

    // Every operation is one short statement against a local file, and the context is scoped, so each
    // call opens a scope of its own rather than holding one for the life of the singleton.
    private T WithDatabase<T>(Func<StorageDbContext, T> work) {
        using IServiceScope scope = scopes.CreateScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();

        return work(database);
    }
}
