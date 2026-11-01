// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Authentication;

/// <summary>Runs the real web host against a throwaway SQLite database.</summary>
internal sealed class AspApplication(int loginPermitLimit = 100, int renderTimeoutSeconds = 5) : WebApplicationFactory<Program> {

    /// <summary>The database this host migrates and serves; it is deleted with the factory.</summary>
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"uk.osric.asp.integration.{Guid.NewGuid():N}.db");

    /// <summary>Counts every compilation the host performs, so a test can prove a cache hit.</summary>
    public RecordingCompiler Compiler { get; } = new(new TemplateCompiler());

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        // Production, so the exception-handler branch is what answers a failure rather than the
        // developer exception page.
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Storage", $"Data Source={DatabasePath}");
        builder.UseSetting("Pages:Render:TimeoutSeconds", renderTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        builder.ConfigureTestServices(services => {
            services.AddSingleton<ITemplateCompiler>(Compiler);
            // The shared fixture signs in many times; the dedicated throttle test lowers this.
            services.Configure<LoginRateLimitOptions>(options => options.PermitLimit = loginPermitLimit);
        });
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
