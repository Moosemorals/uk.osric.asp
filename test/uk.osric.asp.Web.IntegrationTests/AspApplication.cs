// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

/// <summary>Runs the real web host against a throwaway SQLite database.</summary>
internal sealed class AspApplication : WebApplicationFactory<Program> {

    /// <summary>The database this host migrates and serves; it is deleted with the factory.</summary>
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"uk.osric.asp.integration.{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        // Production, so the exception-handler branch is what answers a failure rather than the
        // developer exception page.
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Storage", $"Data Source={DatabasePath}");
    }

    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        if (!disposing) {
            return;
        }

        foreach (string suffix in new[] { "", "-shm", "-wal" }) {
            File.Delete(DatabasePath + suffix);
        }
    }
}
