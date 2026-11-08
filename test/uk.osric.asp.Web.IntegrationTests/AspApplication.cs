// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Metrics;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Users;

/// <summary>Runs the real web host against a throwaway SQLite database.</summary>
/// <param name="loginPermitLimit">Sign-in attempts allowed per window.</param>
/// <param name="renderTimeoutSeconds">The budget for one render.</param>
/// <param name="realPasswordHasher">
/// Keeps the production password hasher instead of <see cref="FastPasswordService"/>. A fixture that
/// is about signing in sets this; the rest sign in only to reach the page they prove.
/// </param>
/// <param name="metricsSource">Stands in for Prometheus, which no fixture can reach.</param>
/// <param name="assistantEnabled">Enables the assistant for the assistant integration fixtures.</param>
/// <param name="chatCompletionClient">Stands in for the configured provider.</param>
/// <param name="pageFetcher">Stands in for the page fetcher, so no fixture needs a socket.</param>
/// <param name="archiveUploadBytes">Overrides the import upload cap, for the too-large fixture.</param>
/// <param name="pageLayout">
/// The layout pages are wrapped in. Off by default so a fixture about one page does not also have to
/// write the layout; <c>SiteChromeTests</c> switches the shipped default on.
/// </param>
internal sealed class AspApplication(
    int loginPermitLimit = 100,
    int renderTimeoutSeconds = 5,
    bool realPasswordHasher = false,
    IMetricsSource? metricsSource = null,
    bool assistantEnabled = false,
    IChatCompletionClient? chatCompletionClient = null,
    IPageFetcher? pageFetcher = null,
    int? archiveUploadBytes = null,
    string? pageLayout = null) : WebApplicationFactory<Program> {

    /// <summary>The database this host migrates and serves; it is deleted with the factory.</summary>
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"uk.osric.asp.integration.{Guid.NewGuid():N}.db");

    /// <summary>Counts every compilation the host performs, so a test can prove a cache hit.</summary>
    public RecordingCompiler Compiler { get; } = new(new PageCompiler());

    /// <summary>Everything the host logged, beside the JSON console the app configures for itself.</summary>
    public LogCapture Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        // Production, so the exception-handler branch is what answers a failure rather than the
        // developer exception page.
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Storage", $"Data Source={DatabasePath}");
        // Applied when the host is built, which is after Program cleared the default providers.
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.UseSetting("Pages:Render:TimeoutSeconds", renderTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Assistant:Enabled", assistantEnabled.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Pages:Layout", pageLayout ?? string.Empty);
        if (assistantEnabled) {
            builder.UseSetting("Assistant:ApiKey", "integration-test-key");
        }

        if (archiveUploadBytes is { } uploadBytes) {
            builder.UseSetting("Pages:Archive:MaxUploadBytes", uploadBytes.ToString(CultureInfo.InvariantCulture));
        }
        builder.ConfigureTestServices(services => {
            services.AddSingleton<IPageCompiler>(Compiler);
            // The shared fixture signs in many times; the dedicated throttle test lowers this.
            services.Configure<LoginRateLimitOptions>(options => options.PermitLimit = loginPermitLimit);

            if (!realPasswordHasher) {
                // Registered last, so it wins over the singleton the host registered.
                services.AddSingleton<IPasswordService, FastPasswordService>();
            }

            if (metricsSource is not null) {
                // The last registration of a single service wins, so the real source is replaced.
                services.AddSingleton(metricsSource);
            }

            if (chatCompletionClient is not null) {
                services.AddSingleton(chatCompletionClient);
            }

            if (pageFetcher is not null) {
                services.AddSingleton(pageFetcher);
            }
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
