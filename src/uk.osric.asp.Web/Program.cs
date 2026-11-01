// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Endpoints;
using uk.osric.asp.Web.Health;
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
        builder.Services.AddScoped<IStorage, SqliteStorage>();
        builder.Services.AddScoped<IUserStore, SqliteUserStore>();
        builder.Services.AddSingleton<IPasswordService, PasswordService>();
        builder.Services.AddSingleton<IUserConsole, SystemUserConsole>();
        builder.Services.AddScoped<PageService>();
        builder.Services.AddSingleton<ITemplateCompiler, TemplateCompiler>();
        builder.Services.AddSingleton<PageMetrics>();
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
        // Pages run for the safe verbs only; anything else is answered 405 by routing rather than
        // by page code that never expected it.
        app.MapMethods("{**path}", ["GET", "POST", "HEAD"], MainEndpoint.Handle);

        await app.RunAsync();
    }
}
