// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using uk.osric.asp.Web.Storage;

/// <summary>Reports whether SQLite is reachable: a host that cannot read storage is not up.</summary>
public sealed class StorageHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck {

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        // The database context is scoped, so each check needs a fresh scope rather than resolving
        // from the root provider.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();

        bool canConnect = await database.Database.CanConnectAsync(cancellationToken);
        if (canConnect) {
            return HealthCheckResult.Healthy("SQLite reachable");
        }

        return HealthCheckResult.Unhealthy("SQLite unreachable");
    }
}
