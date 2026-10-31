// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;
using uk.osric.asp.Web.Health;
using uk.osric.asp.Web.Storage;

[TestFixture]
public sealed class StorageHealthCheckTests {

    [Test]
    public async Task HealthyWhenTheDatabaseOpens() {
        StorageHealthCheck check = new(CreateScopeFactory("Data Source=:memory:"));

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
        Assert.That(result.Description, Is.EqualTo("SQLite reachable"));
    }

    [Test]
    public async Task UnhealthyWhenTheDatabaseCannotOpen() {
        StorageHealthCheck check = new(CreateScopeFactory("Data Source=/no/such/directory/storage.db"));

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
        Assert.That(result.Description, Is.EqualTo("SQLite unreachable"));
    }

    private static IServiceScopeFactory CreateScopeFactory(string connectionString) {
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(connectionString));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
