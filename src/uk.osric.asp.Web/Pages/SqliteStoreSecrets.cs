// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using Microsoft.EntityFrameworkCore;
using System.Text;
using uk.osric.asp.Web.Storage;

/// <summary>The EF Core and raw-SQL implementation of <see cref="IStoreSecrets"/>.</summary>
/// <remarks>
/// A read is one primary-key lookup or a bounded scan of the page's own names, so the obvious
/// statement is the right one. The write is a single statement, so a concurrent writer either fits
/// under the limits or is refused. Every call owns its context, as the other stores do: a render that
/// was abandoned can outlive the request scope.
/// </remarks>
internal sealed class SqliteStoreSecrets(
    IDbContextFactory<StorageDbContext> contexts,
    PageStoresOptions options,
    TimeProvider clock,
    PageStorageRefresh? refresh = null) : IStoreSecrets {

    /// <inheritdoc/>
    public async Task<StoreSecretRow?> FindAsync(
        string subject,
        string name,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrEmpty(name);

        await using StorageDbContext database = Open();

        StoreSecretEntry? found = await database.Set<StoreSecretEntry>().AsNoTracking()
            .FirstOrDefaultAsync(row => row.Subject == subject && row.Name == name, cancellationToken);

        return found is null ? null : Row(found);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StoreSecretRow>> ListAsync(
        string subject,
        int limit,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await using StorageDbContext database = Open();

        List<StoreSecretEntry> rows = await database.Set<StoreSecretEntry>().AsNoTracking()
            .Where(row => row.Subject == subject)
            .OrderBy(row => row.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(Row)];
    }

    /// <inheritdoc/>
    public async Task WriteAsync(
        StoreSecretWrite write,
        PageSecretOptions limits,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(limits);

        string updatedAt = StoreSubjectKeys.Stamp(clock.GetUtcNow());
        await using StorageDbContext database = Open();

        int written = await StoreSecretSql.UpsertAsync(database, write, updatedAt, limits, cancellationToken);

        if (written == 0) {
            throw await RefusalAsync(database, write, limits, cancellationToken);
        }

        refresh?.Request();
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(
        string subject,
        string name,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrEmpty(name);

        await using StorageDbContext database = Open();

        int removed = await database.Set<StoreSecretEntry>()
            .Where(row => row.Subject == subject && row.Name == name)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0) {
            refresh?.Request();
        }

        return removed > 0;
    }

    private static StoreSecretRow Row(StoreSecretEntry row) =>
        new(row.Subject, row.PageKey, row.Name, row.Value, row.UpdatedAt);

    private static async Task<ArgumentException> RefusalAsync(
        StorageDbContext database,
        StoreSecretWrite write,
        PageSecretOptions limits,
        CancellationToken cancellationToken) {
        if (write.RequireLivePage
            && !await database.Set<PageHead>().AnyAsync(
                head => head.Path == write.PageKey && !head.IsDeleted, cancellationToken)) {
            return new ArgumentException(
                $"{write.PageKey} was deleted before its secret could be written; the write was refused.");
        }

        long bytes = Encoding.UTF8.GetByteCount(write.Value);
        if (bytes > limits.MaxSecretBytes) {
            return new ArgumentException(
                $"The secret {write.Name} is {bytes} bytes and Pages:Stores:Secrets:MaxSecretBytes is {limits.MaxSecretBytes}.");
        }

        // A subject holds at most its name limit, so the total is summed over the rows it holds rather
        // than translated: the byte count of a text column is not something SQLite's LENGTH answers.
        List<StoreSecretEntry> held = await database.Set<StoreSecretEntry>().AsNoTracking()
            .Where(row => row.Subject == write.Subject && row.Name != write.Name)
            .ToListAsync(cancellationToken);

        if (held.Count >= limits.MaxSecrets) {
            return new ArgumentException(
                $"The page already holds Pages:Stores:Secrets:MaxSecrets secrets ({limits.MaxSecrets}).");
        }

        long total = held.Sum(
            row => (long)Encoding.UTF8.GetByteCount(row.Name) + Encoding.UTF8.GetByteCount(row.Value));
        long added = Encoding.UTF8.GetByteCount(write.Name) + bytes;

        return new ArgumentException(
            $"The page would need {total + added} bytes of secrets and may use Pages:Stores:Secrets:MaxSubjectBytes ({limits.MaxSubjectBytes}).");
    }

    private StorageDbContext Open() {
        StorageDbContext database = contexts.CreateDbContext();
        database.Database.SetCommandTimeout(options.CommandTimeout);

        return database;
    }
}
