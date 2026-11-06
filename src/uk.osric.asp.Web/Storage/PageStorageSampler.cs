// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using uk.osric.asp.Web.Pages;

/// <summary>Samples the database file's size and records it for the status page.</summary>
internal sealed class PageStorageSampler(
    IServiceScopeFactory scopes,
    PageMetrics metrics,
    PageStorageRefresh refresh,
    ILogger<PageStorageSampler> logger) : BackgroundService {

    public async Task SampleAsync(CancellationToken cancellationToken) {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
            await database.Database.OpenConnectionAsync(deadline.Token);
            DbConnection connection = database.Database.GetDbConnection();
            (long payload, long allocated) = await ReadSizesAsync(connection, deadline.Token);
            long? capacity = null;
            if (connection.DataSource.Length > 0 && connection.DataSource != ":memory:") {
                string path = Path.GetFullPath(connection.DataSource);
                DriveInfo? drive = DriveInfo.GetDrives()
                    .Where(candidate => IsOnVolume(path, candidate.RootDirectory.FullName))
                    .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
                    .FirstOrDefault();
                capacity = drive?.TotalSize;
            }

            metrics.RecordStorage(payload, allocated, capacity);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception exception) {
            logger.LogWarning(exception, "Storage telemetry sample failed; retaining the last successful values.");
        }
    }

    public static async Task<(long Payload, long Allocated)> ReadSizesAsync(DbConnection connection, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(connection);
        using CancellationTokenRegistration interrupt = cancellationToken.Register(() => {
            if (connection is SqliteConnection sqlite) {
                SQLitePCL.raw.sqlite3_interrupt(sqlite.Handle);
            }
        });
        await using DbCommand command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = """
            SELECT COALESCE(SUM(
                length(CAST(RevisionId AS BLOB)) + length(CAST(Path AS BLOB)) +
                length(CAST(Hash AS BLOB)) + length(CAST(SavedAt AS BLOB)) +
                length(CAST(Template AS BLOB)) + length(CAST(CreationRuntimeId AS BLOB)) +
                COALESCE(length(CAST(TemplateHash AS BLOB)), 0) +
                COALESCE(length(CAST(RuntimeId AS BLOB)), 0) +
                COALESCE(length(CAST(Diagnostics AS BLOB)), 0) +
                COALESCE(length(Image), 0) + COALESCE(length(Symbols), 0)), 0)
            FROM PageRevisions;
            """;
        long payload = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        command.CommandText = """
            SELECT COALESCE(SUM(
                length(CAST(PagePath AS BLOB)) + length(CAST(Key AS BLOB)) +
                length(CAST(Value AS BLOB)) + length(CAST(UpdatedAt AS BLOB))), 0)
            FROM PageState;
            """;
        payload += Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        command.CommandText = "PRAGMA page_count";
        long count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        command.CommandText = "PRAGMA page_size";
        long size = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return (payload, checked(count * size));
    }

    public static bool IsOnVolume(string databasePath, string root) {
        ArgumentNullException.ThrowIfNull(databasePath);
        ArgumentNullException.ThrowIfNull(root);
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return databasePath.StartsWith(prefix, StringComparison.Ordinal);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            await SampleAsync(stoppingToken);
            await refresh.WaitAsync(stoppingToken);
        }
    }
}
