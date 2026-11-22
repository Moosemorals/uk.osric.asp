// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Collections.Frozen;
using System.Data;
using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Storage;

/// <summary>What a record store needs of the catalogue: the source's tables and the database.</summary>
internal interface IRecordCatalog {

    /// <summary>The connection string the sandbox opens its own unpooled connection with.</summary>
    string ConnectionString { get; }

    /// <summary>Reads the catalogue rows one source declared.</summary>
    /// <param name="source">The declaring source's key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The tables, in the order they were made.</returns>
    Task<IReadOnlyList<DeclaredTable>> ListAsync(PagePath source, CancellationToken cancellationToken = default);
}

/// <summary>The catalogue over the storage context, as the record store reads it.</summary>
/// <remarks>
/// The store owns its connection, its views and its authorizer, so it must not borrow the request's
/// context: this is the one read that goes through it, and it happens once per handle.
/// </remarks>
internal sealed class SqliteRecordCatalog(IDbContextFactory<StorageDbContext> factory) : IRecordCatalog {

    private string? _connectionString;

    /// <inheritdoc/>
    public string ConnectionString => _connectionString ??= Read();

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DeclaredTable>> ListAsync(PagePath source, CancellationToken cancellationToken = default) {
        await using StorageDbContext database = factory.CreateDbContext();

        return await DeclaredTableApplier.ListAsync(database, source, cancellationToken);
    }

    private string Read() {
        using StorageDbContext database = factory.CreateDbContext();

        return database.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The storage database has no connection string, so a page cannot have a store.");
    }
}
