// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Health;
using uk.osric.asp.Web.Metrics;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;
using uk.osric.asp.Web.Users;

// Not static: WebApplicationFactory<Program> needs a type to name when it looks for the entry point.
// Internal, like the rest of the shell; the integration tests reach it through InternalsVisibleTo.
/// <summary>Imperative shell: wires the web host to the functional core.</summary>
internal class Program {
    public static async Task Main(string[] args) {
        bool isUserCommand = UserCommands.IsCommand(args);
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            isUserCommand ? [] : args);

        if (isUserCommand) {
            // The command's output is its result; migration chatter belongs in the log, not on stdout.
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        }
        string connectionString = builder.Configuration.GetConnectionString("Storage")
            ?? throw new InvalidOperationException("ConnectionStrings:Storage must be configured.");
        SqliteConnectionStringBuilder connection = new(connectionString);
        if (connection.DataSource.Length > 0 && connection.DataSource != ":memory:") {
            connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)
                ?? throw new InvalidOperationException("The storage database must have a parent directory."));
        }

        builder.Services.AddDbContext<StorageDbContext>(options => options.UseSqlite(connection.ToString()));
        builder.Services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage")
            .AddCheck<AbandonedRenderHealthCheck>("pages");
        builder.Services.AddScoped<IPageStore, SqlitePageStore>();
        builder.Services.AddScoped<IUserStore, SqliteUserStore>();
        builder.Services.AddSingleton<IPasswordService, PasswordService>();
        builder.Services.AddSingleton<IUserConsole, SystemUserConsole>();
        builder.Services.AddScoped<TemplateCompiler>();
        builder.Services.AddScoped<PageRenderer>();
        builder.Services.AddScoped<PageEditor>();
        builder.Services.AddSingleton<IPageCompiler, PageCompiler>();
        builder.Services.AddSingleton<PageMetrics>();
        builder.Services.AddSingleton(services => MetricsOptions.FromConfiguration(
            builder.Configuration,
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Metrics")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IMetricsSource>(services => {
            MetricsOptions options = services.GetRequiredService<MetricsOptions>();
            // The per-query deadline belongs to the source: one slow query must not hold the page.
            return new PrometheusMetricsSource(new HttpClient {
                BaseAddress = options.PrometheusUrl,
                Timeout = Timeout.InfiniteTimeSpan,
            });
        });
        builder.Services.AddSingleton<MetricsDashboard>();
        // Metrics only: registering no instrumentation package means no tracer or logger provider.
        // The exporter serves /metrics on this listener; Caddy refuses that path publicly.
        _ = builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(PageMetrics.MeterName)
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Runtime")
            .AddView("page.render.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.render.wait", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.compile.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.handler.load.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.handler.invoke.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.save.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.image.size", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.ImageBuckets })
            .AddPrometheusExporter());
        builder.Services.AddSingleton(RenderOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton(CompileOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton<PageBudget>();
        builder.Services.AddSingleton(services => new PageHealth(
            services.GetRequiredService<RenderOptions>().AbandonedLimit));
        builder.Services.AddSingleton(new ReservedPaths(
            builder.Configuration.GetSection("Pages:ReservedPaths").Get<string[]>()));
        builder.Services.AddEditorAuthentication(builder.Environment);
        builder.Services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            // Caddy terminates TLS on the host loopback and is the only peer that can reach the port
            // (the quadlet publishes 127.0.0.1:8080), so any peer may describe the original request.
            // The loopback-only defaults do not survive podman's user-space networking, so the publish
            // is what bounds this trust, not an address list.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });

        WebApplication app = builder.Build();
        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope()) {
            StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
            await database.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
        }

        if (isUserCommand) {
            await using (app) {
                Environment.ExitCode = await UserCommands.RunAsync(
                    args[1..], app.Services, app.Lifetime.ApplicationStopping);
                return;
            }
        }

        // The compiler's reference set comes from the runtime, so resolve it before the host listens:
        // a runtime that cannot supply one must fail the deploy, not the first page that gets saved.
        _ = app.Services.GetRequiredService<IPageCompiler>();

        // Before anything that reads the scheme. Without it every request arrives as plain HTTP from
        // the proxy, so HSTS emits nothing and IsHttps is always false.
        app.UseForwardedHeaders();

        if (app.Environment.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        } else {
            // An inline branch keeps the error page out of the router. Re-executing a path would run
            // the failing request's method again, so a failed POST could be answered by the wrong endpoint.
            app.UseExceptionHandler(errorApplication => errorApplication.Run(ErrorEndpoint.HandleAsync));
            app.UseHsts();
        }

        app.UseSession();
        app.UseStaticFiles();

        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapGet("/status", StatusEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapGet("/edit/{**path}", EditorEndpoint.Handler)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapPost("/edit/{**path}", EditorEndpoint.Handler)
            .RequireAuthorization(EditorPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        app.MapGet("/login", LoginEndpoint.GetAsync).AllowAnonymous();
        app.MapPost("/login", LoginEndpoint.PostAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        app.MapPost("/logout", LoginEndpoint.LogoutAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        // The health route is mapped before the page catch-all, so a page stored under /heath can
        // never shadow it; /heath is also in Pages:ReservedPaths.
        app.MapHealthChecks("/heath", new HealthCheckOptions {
            ResponseWriter = HealthReportWriter.WriteAsync,
        });
        // The scrape target is Prometheus inside the podman network; /metrics is reserved so a page
        // cannot claim the key, and Caddy answers 404 for it on the public origin.
        app.MapPrometheusScrapingEndpoint("/metrics");
        // Pages run for the safe verbs only; anything else is answered 405 by routing rather than
        // by page code that never expected it.
        app.MapMethods("{**path}", ["GET", "POST", "HEAD"], PageEndpoint.Handle);

        await app.RunAsync();
    }
}
