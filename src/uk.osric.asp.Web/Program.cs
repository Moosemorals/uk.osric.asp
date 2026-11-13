// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using System.Net;
using System.Text.Json;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Health;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Logging;
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

        // The site is behind Caddy; nothing on this hop needs to advertise the server that ran it.
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

        // The journal is the sink: podman hands the container's stdout to systemd a line at a time, so
        // one single-line JSON object is one entry with its fields intact. The formatter is chosen
        // here rather than in configuration because it is the shape of the output, not a deployment
        // setting - and clearing the providers first is what stops the framework's text console from
        // writing a second copy of every line beside it. Levels still come from appsettings.
        // See docs/logging.md.
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options => {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });

        if (isUserCommand) {
            // The command's output is its result; migration chatter belongs in the log, not on stdout.
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        }
        string connectionString = builder.Configuration.GetConnectionString("Storage")
            ?? throw new InvalidOperationException("ConnectionStrings:Storage must be configured.");
        SqliteConnectionStringBuilder connection = [with(connectionString)];
        if (connection.DataSource.Length > 0 && connection.DataSource != ":memory:") {
            connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)
                ?? throw new InvalidOperationException("The storage database must have a parent directory."));
        }
        bool assistantEnabled = builder.Configuration.GetValue<bool>("Assistant:Enabled");

        // Configuration: the database, its options, and the proxy trust the host depends on.
        // The scoped context is what the stores on the request inject; the factory is what lets a
        // page's storage call own its context and outlive the request scope safely. The options are
        // singletons because the factory is: a singleton cannot consume a scoped dependency.
        builder.Services.AddDbContext<StorageDbContext>(
            options => options.UseSqlite(connection.ToString()),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        builder.Services.AddDbContextFactory<StorageDbContext>(options => options.UseSqlite(connection.ToString()));

        builder.Services.AddOptions<AssistantOptions>()
            .Bind(builder.Configuration.GetSection("Assistant"))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<AssistantOptions>, AssistantOptionsValidator>();
        // Bound whether or not the assistant is switched on: the memory store reads its caps from it,
        // and an account removal sweeps memory even when the assistant is disabled.
        builder.Services.AddSingleton(services => services.GetRequiredService<IOptions<AssistantOptions>>().Value);

        // The upload limits and the framework's form parser come from the same cap, so an oversized
        // body is refused rather than buffered, and a normal one never spills to a temporary file.
        builder.Services.Configure<FormOptions>(options => {
            ArchiveOptions archive = ArchiveOptions.FromConfiguration(builder.Configuration);
            options.MultipartBodyLengthLimit = archive.MaxUploadBytes + (64 * 1024);
            options.MemoryBufferThreshold = archive.MaxUploadBytes + (64 * 1024);
            options.ValueCountLimit = 1024;
            options.MultipartHeadersLengthLimit = 16 * 1024;
        });

        builder.Services.Configure<ForwardedHeadersOptions>(options => {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            // Caddy terminates TLS on the host loopback and is the only peer that can reach the port
            // (the quadlet publishes 127.0.0.1:8080), so any peer may describe the original request.
            // The loopback-only defaults do not survive podman's user-space networking, so the publish
            // is what bounds this trust, not an address list. Caddy replaces client-supplied
            // X-Forwarded-* rather than passing them through, and the middleware reads the rightmost
            // entry, so the address is the browser's and cannot be chosen by the request.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });

        builder.Services.AddHostedService<PageStorageSampler>();

        builder.Services.AddSingleton(CompileOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton(services => new PageHealth(
            services.GetRequiredService<RenderOptions>().AbandonedLimit));
        builder.Services.AddSingleton(PageLayoutConfiguration.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton(new ReservedPaths(
            builder.Configuration.GetSection("Pages:ReservedPaths").Get<string[]>()));
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddSingleton<IPageCompiler, PageCompiler>();
        builder.Services.AddSingleton<MarkdownRenderer>();
        builder.Services.AddSingleton<DiffRenderer>();
        builder.Services.AddSingleton<IPasswordService, PasswordService>();
        builder.Services.AddSingleton<IUserConsole, SystemUserConsole>();
        builder.Services.AddSingleton<PageBudget>();
        builder.Services.AddSingleton<PageMetrics>();
        builder.Services.AddSingleton<PageStorageRefresh>();
        builder.Services.AddSingleton(RenderOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton(services => PageContentTypesConfiguration.FromConfiguration(
            builder.Configuration,
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Pages.ContentTypes")));
        builder.Services.AddSingleton(PageStorageOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton(PageSessionOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton(PageSessionCookieOptions.For(builder.Environment));
        builder.Services.AddSingleton<IPageSessionStore, SqlitePageSessionStore>();
        builder.Services.AddHostedService<PageSessionCleanupService>();
        builder.Services.AddSingleton(ArchiveOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton(PageReadOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton<IPageStateStore, SqlitePageStateStore>();
        builder.Services.AddSingleton<IPageReadStore, SqlitePageReadStore>();
        builder.Services.AddSingleton<IPageRevisionStore, SqlitePageRevisionStore>();
        builder.Services.AddSingleton(PageFetchOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddHttpClient(OutboundFetcher.ClientName)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler {
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });
        builder.Services.AddSingleton<OutboundFetcher>();
        builder.Services.AddSingleton<IPageFetcher, PageFetcher>();
        builder.Services.AddSingleton(services => new PageEnvironment(
            services.GetRequiredService<ReservedPaths>(),
            services.GetRequiredService<PageLayoutConfiguration>(),
            services.GetRequiredService<RenderOptions>(),
            services.GetRequiredService<PageContentTypes>(),
            services.GetRequiredService<PageStorageOptions>(),
            services.GetRequiredService<PageFetchOptions>(),
            services.GetRequiredService<PageReadOptions>(),
            services.GetRequiredService<IPageStateStore>(),
            services.GetRequiredService<IPageReadStore>(),
            services.GetRequiredService<PageSaver>(),
            services.GetRequiredService<IPageFetcher>(),
            services.GetRequiredService<PageMetrics>(),
            services.GetRequiredService<IAntiforgery>(),
            services.GetRequiredService<MarkdownRenderer>(),
            services.GetRequiredService<DiffRenderer>(),
            services.GetRequiredService<PageSessionOptions>(),
            services.GetRequiredService<IPageSessionStore>(),
            services.GetRequiredService<PageSessionCookieOptions>()));

        builder.Services.AddScoped<IConversationStore, SqliteConversationStore>();
        // Registered whether or not the assistant is enabled: the tables are migrated either way, and
        // `users remove` sweeps an account's memories even when the assistant is switched off.
        builder.Services.AddScoped<IMemoryStore, SqliteMemoryStore>();
        // The listing judges an image's state against the running build, so the store is told which
        // one that is; a page whose image came from an earlier build says Stale until it is requested.
        builder.Services.AddScoped<IPageStore>(services => new SqlitePageStore(
            services.GetRequiredService<StorageDbContext>(),
            services.GetRequiredService<ILogger<SqlitePageStore>>(),
            services.GetRequiredService<PageStorageRefresh>(),
            services.GetRequiredService<IPageCompiler>().RuntimeId));
        builder.Services.AddScoped<IPageBatchStore, SqlitePageBatchStore>();
        builder.Services.AddScoped<PageArchiveReader>();
        builder.Services.AddScoped<ImportProcessor>();
        builder.Services.AddSingleton<ImportStageStore>();
        builder.Services.AddSingleton<ImportQueue>();
        builder.Services.AddHostedService<ImportWorker>();
        builder.Services.AddScoped<IUserStore, SqliteUserStore>();
        builder.Services.AddScoped<PageEditor>();
        // The save is a singleton because a page's save can outlive the request that started it, and
        // the image builder has no per-request state: a compiler, the compile budget and the meters,
        // all singletons already.
        builder.Services.AddSingleton<PageSaver>();
        builder.Services.AddSingleton<PageImageBuilder>();
        builder.Services.AddScoped<PageRenderer>();

        if (assistantEnabled) {
            builder.Services.AddHostedService<RunWorker>();
            builder.Services.AddHostedService<RunJournalCleanupService>();

            builder.Services.AddSingleton<AssistantMetrics>();
            builder.Services.AddSingleton<IAssistantFetcher, AssistantFetcher>();
            builder.Services.AddSingleton<PageToolExecutor>();
            builder.Services.AddSingleton<PagePathPolicy>();
            builder.Services.AddSingleton<RunQueue>();
            builder.Services.AddSingleton<RunRegistry>();
            builder.Services.AddSingleton<ToolApprovalCoordinator>();
            builder.Services.AddSingleton<RunContinuationCoordinator>();

            builder.Services.AddScoped<RunProcessor>();

            builder.Services.AddHttpClient<IChatCompletionClient, OpenAiChatCompletionClient>(client => {
                client.Timeout = Timeout.InfiniteTimeSpan;
            }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        }

        builder.Services.AddEditorAuthentication(builder.Environment);

        builder.Services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage")
            .AddCheck<AbandonedRenderHealthCheck>("pages");

        builder.Services.AddSingleton(services => MetricsOptions.FromConfiguration(
            builder.Configuration,
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Metrics")));
        builder.Services.AddSingleton<IMetricsSource>(services => {
            MetricsOptions options = services.GetRequiredService<MetricsOptions>();
            // The per-query deadline belongs to the source: one slow query must not hold the page.
            return new PrometheusSource(
                new HttpClient {
                    BaseAddress = options.PrometheusUrl,
                    Timeout = Timeout.InfiniteTimeSpan,
                },
                services.GetRequiredService<ILogger<PrometheusSource>>());
        });
        builder.Services.AddSingleton<ChartRenderer>();
        builder.Services.AddSingleton<Dashboard>();

        // Metrics only: registering no instrumentation package means no tracer or logger provider.
        // The exporter serves /metrics on this listener; Caddy refuses that path publicly.
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(PageMetrics.MeterName)
            .AddMeter(AssistantMetrics.MeterName)
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Runtime")
            .AddView("page.render.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.render.wait", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.compile.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.load.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.invoke.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.save.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.import.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.fetch.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.image.size", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.ImageBuckets })
            .AddView("assistant.run.duration", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.RunDurationBuckets })
            .AddView("assistant.provider.first_token.duration", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.ProviderBuckets })
            .AddView("assistant.provider.request.duration", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.ProviderBuckets })
            .AddView("assistant.tool.duration", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.ToolDurationBuckets })
            .AddView("assistant.run.steps", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.StepBuckets })
            .AddView("assistant.run.tool_calls", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.ToolCallBuckets })
            .AddView("assistant.memory.recalled", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.MemoryCountBuckets })
            .AddView("assistant.memory.chars", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.MemoryCharBuckets })
            .AddPrometheusExporter());

        WebApplication app = builder.Build();
        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope()) {
            StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
            await database.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
            if (assistantEnabled) {
                IConversationStore assistantConversations = scope.ServiceProvider.GetRequiredService<IConversationStore>();
                await assistantConversations.RecoverInterruptedRunsAsync(app.Lifetime.ApplicationStopping);
            }
            IPageStore pages = scope.ServiceProvider.GetRequiredService<IPageStore>();
            await PageAssetSeeder.SeedAsync(pages, app.Lifetime.ApplicationStopping);
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

        // A malformed content-type map is the same trade: the site does not come up with half a map,
        // and the journal names the offending key before anything is served.
        _ = app.Services.GetRequiredService<PageContentTypes>();

        // Before anything that reads the scheme. Without it every request arrives as plain HTTP from
        // the proxy, so HSTS emits nothing and IsHttps is always false.
        app.UseForwardedHeaders();

        // Ahead of the exception handler, so the handler's own report of an unhandled exception - and
        // the error page it renders - are written inside the request's scope.
        app.UseRequestLogScope();

        // Before the exception handler, so a request that throws still gets the default header set.
        app.Use(SecurityHeaders.ApplyAsync);

        if (app.Environment.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        } else {
            // An inline branch keeps the error page out of the router. Re-executing a path would run
            // the failing request's method again, so a failed POST could be answered by the wrong endpoint.
            app.UseExceptionHandler(errorApplication => errorApplication.Run(ErrorEndpoint.HandleAsync));
            app.UseHsts();
        }

        // The guarantee that nobody gets a blank body: an endpoint, the framework, a routing 404/405
        // or the rate limiter can all answer with a status and nothing else, and this turns that into
        // a document a person can read or a problem document a program can parse. A response that
        // already carries a body, a content type or a length is left alone, so nothing is written twice.
        app.UseStatusCodePages(async statusContext => {
            HttpContext failed = statusContext.HttpContext;

            // A rejected sign-in form is recoverable: show the form again with a fresh token rather
            // than a page describing a dead end. Every other unclaimed status gets the mapped page.
            if (failed.Response.StatusCode == StatusCodes.Status400BadRequest
                && HttpMethods.IsPost(failed.Request.Method)
                && failed.Request.Path.StartsWithSegments("/login")) {
                await LoginEndpoint.WriteExpiredFormAsync(
                    failed,
                    failed.RequestServices.GetRequiredService<IAntiforgery>());
                return;
            }

            bool isAuthenticated = failed.User.Identity?.IsAuthenticated == true;
            ErrorMessage message = failed.Response.StatusCode == StatusCodes.Status400BadRequest
                && HttpMethods.IsPost(failed.Request.Method)
                    ? ErrorMessage.FormExpired(isAuthenticated)
                    : ErrorMessage.ForStatus(failed.Response.StatusCode, isAuthenticated);

            await ErrorResponses.WriteAsync(failed, failed.Response.StatusCode, message);
        });

        // Order matters: routing first, so the limiter and identity read endpoint metadata, then
        // authorization, then the antiforgery check that guards the unsafe ones.
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        // After authentication and routing: the account and the route's ids are the two facts the
        // scope could not carry when it opened.
        app.UseRequestLogSubject();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapGet("/status", StatusEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapGet("/status/chart", ChartEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name);

        app.MapGet("/edit/{**path}", EditorEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapPost("/edit/{**path}", EditorEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        app.MapGet("/admin/changes", ChangesEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapGet("/admin/export", ExportEndpoint.ListAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapPost("/admin/export", ExportEndpoint.DownloadAsync)
            .RequireAuthorization(EditorPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        app.MapGet("/admin/import", ImportEndpoint.UploadFormAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapPost("/admin/import", ImportEndpoint.UploadAsync)
            .RequireAuthorization(EditorPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        app.MapGet("/admin/import/{id}", ImportEndpoint.ReviewAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapPost("/admin/import/{id}", ImportEndpoint.ApproveAsync)
            .RequireAuthorization(EditorPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        app.MapGet("/admin/import/{id}/progress", ImportEndpoint.ProgressAsync)
            .RequireAuthorization(EditorPolicy.Name);

        app.MapGet("/login", LoginEndpoint.GetAsync).AllowAnonymous();
        app.MapPost("/login", LoginEndpoint.PostAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        app.MapGet("/logout", LoginEndpoint.LogoutConfirmationAsync);
        app.MapPost("/logout", LoginEndpoint.LogoutAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        app.MapHealthChecks("/heath", new HealthCheckOptions {
            ResponseWriter = ReportWriter.WriteAsync,
        });

        if (assistantEnabled) {
            app.MapGet("/assistant", AssistantEndpoint.ListAsync)
                .RequireAuthorization(EditorPolicy.Name);
            app.MapPost("/assistant", AssistantEndpoint.ListAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
            app.MapGet("/assistant/{id:long}", AssistantEndpoint.ConversationAsync)
                .RequireAuthorization(EditorPolicy.Name);
            app.MapPost("/assistant/{id:long}/messages", AssistantEndpoint.MessagesAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
            app.MapGet("/assistant/{id:long}/messages/{messageId:long}/content", AssistantEndpoint.MessageContentAsync)
                .RequireAuthorization(EditorPolicy.Name);
            app.MapGet("/assistant/{id:long}/events", AssistantEndpoint.EventsAsync)
                .RequireAuthorization(EditorPolicy.Name);
            app.MapPost("/assistant/{id:long}/stop", AssistantEndpoint.StopAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
            app.MapPost("/assistant/{id:long}/runs/{runId}/tool-calls/{toolCallId}/approval", AssistantEndpoint.ApprovalAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
            app.MapPost("/assistant/{id:long}/runs/{runId}/continue", AssistantEndpoint.ContinueAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
            app.MapPost("/assistant/{id:long}/options", AssistantEndpoint.OptionsAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
            app.MapGet("/assistant/{id:long}/delete", AssistantEndpoint.DeleteConfirmationAsync)
                .RequireAuthorization(EditorPolicy.Name);
            app.MapPost("/assistant/{id:long}/delete", AssistantEndpoint.DeleteAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
            // The owner's memory: a page of its own beside the conversations. `{id:long}` binds digits,
            // so `/assistant/memory` never competes with `/assistant/{id:long}`.
            app.MapGet(AssistantMemoryEndpoint.Path, AssistantMemoryEndpoint.ListAsync)
                .RequireAuthorization(EditorPolicy.Name);
            app.MapPost("/assistant/memory/{id:long}", AssistantMemoryEndpoint.SaveAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
            app.MapGet("/assistant/memory/{id:long}/forget", AssistantMemoryEndpoint.ForgetConfirmationAsync)
                .RequireAuthorization(EditorPolicy.Name);
            app.MapPost("/assistant/memory/{id:long}/forget", AssistantMemoryEndpoint.ForgetAsync)
                .RequireAuthorization(EditorPolicy.Name)
                .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        } else {
            app.MapMethods("/assistant", ["GET", "POST", "HEAD"], AssistantDisabled);
            app.MapMethods("/assistant/{**path}", ["GET", "POST", "HEAD"], AssistantDisabled);
        }
        // The scrape target is Prometheus inside the podman network; /metrics is reserved so a page
        // cannot claim the key, and Caddy answers 404 for it on the public origin.
        app.MapPrometheusScrapingEndpoint("/metrics");
        // Pages run for GET, POST and HEAD only; anything else is answered 405 by routing rather than
        // by page code that never expected it.
        app.MapMethods("{**path}", ["GET", "POST", "HEAD"], PageEndpoint.HandleAsync);

        // One line so a restart is visible without reading the framework's startup chatter, and so a
        // host that came up with the assistant switched off says so.
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger<Program>()
            .LogInformation("uk.osric.asp starting in {EnvironmentName} with the assistant {AssistantState}",
                app.Environment.EnvironmentName, assistantEnabled ? "enabled" : "disabled");

        await app.RunAsync();
    }

    private static Task AssistantDisabled(HttpContext context) {
        bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        ErrorMessage message = new(
            "assistant.disabled",
            "The assistant is not available",
            isAuthenticated
                ? "The assistant is turned off on this site. Turn it on with Assistant:Enabled in configuration, then restart."
                : "The assistant is turned off on this site.",
            ErrorMessage.DefaultRemedies(isAuthenticated));

        return ErrorResponses.WriteAsync(context, StatusCodes.Status404NotFound, message);
    }
}
