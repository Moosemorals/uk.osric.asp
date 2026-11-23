// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using System.Net;
using System.Text.Json;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Documentation;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Health;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Logging;
using uk.osric.asp.Web.Metrics;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Shell;
using uk.osric.asp.Web.Storage;
using uk.osric.asp.Web.Users;
using uk.osric.asp.Web.Work;

// Not static: WebApplicationFactory<Program> needs a type to name when it looks for the entry point.
// Internal, like the rest of the shell; the integration tests reach it through InternalsVisibleTo.
/// <summary>Imperative shell: wires the web host to the functional core.</summary>
internal class Program {
    public static async Task<int> Main(string[] args) {
        try {
            await RunAsync(args);
            return 0;
        } catch (ConfigurationException exception) {
            await Console.Error.WriteLineAsync(exception.Message);
            return 1;
        }
    }

    private static async Task RunAsync(string[] args) {
        bool isUserCommand = UserCommands.IsCommand(args);
        bool isSeedsCommand = SeedCommands.IsCommand(args);
        bool isCommand = isUserCommand || isSeedsCommand;
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            isCommand ? [] : args);

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
            options.JsonWriterOptions = new JsonWriterOptions {
                Indented = false,
                Encoder = JsonSerialization.Encoder,
            };
        });

        if (isSeedsCommand) {
            // A pull's result is a tar and stdout is where it goes. One log line landing there would
            // corrupt the archive, so every entry is written to standard error instead.
            builder.Services.Configure<ConsoleLoggerOptions>(
                options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        }

        if (isCommand) {
            // The command's output is its result; migration chatter belongs in the log, not on stdout.
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        }
        // Every value the site needs is read here, through one reader, before anything is built: every
        // section's problems are collected before the report is made, so an operator fixes one list
        // rather than finding one section per restart.
        ConfigurationReader settings = new(builder.Configuration, "ConnectionStrings");
        string connectionString = settings.Text("Storage",
            "a SQLite connection string such as Data Source=data/storage.db");

        SqliteConnectionStringBuilder connection = [with(connectionString)];
        if (connection.DataSource.Length > 0 && connection.DataSource != ":memory:") {
            connection.DataSource = Path.GetFullPath(connection.DataSource, builder.Environment.ContentRootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)
                ?? throw new InvalidOperationException("The storage database must have a parent directory."));
        }

        AdminOptions admin = AdminOptions.FromConfiguration(settings.At("Admin"));
        ArchiveOptions archive = ArchiveOptions.FromConfiguration(settings.At("Pages:Archive"));
        AssistantOptions assistant = AssistantOptions.FromConfiguration(settings.At("Assistant"));
        AuthenticationOptions authentication = AuthenticationOptions.FromConfiguration(settings.At("Authentication"));
        CompileOptions compile = CompileOptions.FromConfiguration(settings.At("Pages:Compile"));
        MetricsOptions metrics = MetricsOptions.FromConfiguration(settings.At("Metrics"));
        RenderOptions render = RenderOptions.FromConfiguration(settings.At("Pages:Render"));
        PageContentTypes contentTypes = PageContentTypesConfiguration.FromConfiguration(settings.At("Pages:ContentTypes"));
        PageEditorOptions editor = PageEditorOptions.FromConfiguration(settings.At("Pages:Editor"));
        PageFetchOptions fetch = PageFetchOptions.FromConfiguration(settings.At("Pages:Fetch"));
        PageLayoutOptions layout = PageLayoutOptions.FromConfiguration(settings.At("Pages:Layout"));
        PageReadOptions read = PageReadOptions.FromConfiguration(settings.At("Pages:Read"));
        PageSearchOptions search = PageSearchOptions.FromConfiguration(settings.At("Pages:Search"));
        PageJobOptions jobOptions = PageJobOptions.FromConfiguration(settings.At("Pages:Jobs"));
        PageStoresOptions stores = PageStoresOptions.FromConfiguration(settings.At("Pages:Stores"));
        SiteOptions site = SiteOptions.FromConfiguration(settings.At("Site"));
        StreamOptions stream = StreamOptions.FromConfiguration(settings.At("Pages:Stream"));

        // The reserved keys are the shell's route table, not a setting. A leftover section from the
        // release before this one is refused rather than ignored: an operator who kept it believes
        // it still narrows or widens something, and it does neither.
        ConfigurationReader pageSettings = settings.At("Pages");
        pageSettings.FailIf(pageSettings.Has("ReservedPaths"), "ReservedPaths",
            "is no longer read. The shell's own route table is the reserved list, so remove the key and restart");
        pageSettings.FailIf(pageSettings.Has("Storage"), "Storage",
            "is no longer read. Pages:Stores carries the page's value limits, so remove the key and restart");
        pageSettings.FailIf(pageSettings.Has("Session"), "Session",
            "is no longer read. Pages:Stores:Visitor carries the session's limits and idle bounds, so remove the key and restart");

        settings.Complete();
        ReservedPaths reserved = new(ReservedPaths.Default);

        // The store's busy timeout is the page's, not the client's thirty-second default: a commit
        // that cannot take the writer answers "busy" after the time the owner chose.
        connection.DefaultTimeout = stores.BusyTimeoutSeconds;

        // Configuration: the database, its options, and the proxy trust the host depends on.
        // The scoped context is what the stores on the request inject; the factory is what lets a
        // page's storage call own its context and outlive the request scope safely. The options are
        // singletons because the factory is: a singleton cannot consume a scoped dependency.
        builder.Services.AddDbContext<StorageDbContext>(
            options => options.UseSqlite(connection.ToString()),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        builder.Services.AddDbContextFactory<StorageDbContext>(options => options.UseSqlite(connection.ToString()));

        // The upload limits and the framework's form parser come from the same limit, so an oversized
        // body is refused rather than buffered, and a normal one never spills to a temporary file.
        builder.Services.Configure<FormOptions>(options => {
            options.MemoryBufferThreshold = archive.MaxUploadBytes + (64 * 1024);
            options.MultipartBodyLengthLimit = archive.MaxUploadBytes + (64 * 1024);
            options.MultipartHeadersLengthLimit = 16 * 1024;
            options.ValueCountLimit = 1024;
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

        // Hosted services
        builder.Services.AddHostedService<PageStorageSampler>();
        builder.Services.AddHostedService<PageSessionCleanupService>();
        builder.Services.AddHostedService<ImportWorker>();
        builder.Services.AddHostedService<PageJobRunner>();
        builder.Services.AddHostedService<RunJournalCleanupService>();
        builder.Services.AddHostedService<RunWorker>();
        builder.Services.AddHostedService<AccountBalanceSampler>();

        // Stuff with their own registration extensions
        builder.Services.AddEditorAuthentication(builder.Environment, authentication);
        builder.Services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage")
            .AddCheck<AbandonedRenderHealthCheck>("pages");
        builder.Services.AddHttpClient(OutboundFetcher.ClientName)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler {
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });
        builder.Services.AddHttpClient<IChatCompletionClient, OpenAiChatCompletionClient>(client => {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddHttpClient<IProviderModelsClient, ProviderModelsClient>(client => {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        builder.Services.AddHttpClient<IAccountBalanceClient, AccountBalanceClient>(client => {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            // The sampler logs the reason when it changes; the handler's own start and end lines for
            // every sample would be a minute's worth of log from a provider that never answers.
            .RemoveAllLoggers();

        // Options types, sort alphabetically so the list is easy to scan.
        builder.Services.AddSingleton(admin);
        builder.Services.AddSingleton(archive);
        builder.Services.AddSingleton(assistant);
        builder.Services.AddSingleton(authentication);
        builder.Services.AddSingleton(compile);
        builder.Services.AddSingleton(contentTypes);
        builder.Services.AddSingleton(editor);
        builder.Services.AddSingleton(jobOptions);
        builder.Services.AddSingleton(layout);
        builder.Services.AddSingleton(metrics);
        builder.Services.AddSingleton(read);
        builder.Services.AddSingleton(search);
        builder.Services.AddSingleton(render);
        builder.Services.AddSingleton(reserved);
        builder.Services.AddSingleton(site);
        builder.Services.AddSingleton(stores);
        builder.Services.AddSingleton(stream);
        builder.Services.AddSingleton(fetch);

        // Singletons, sort alphabetically so the list is easy to scan.
        builder.Services.AddSingleton(PageSessionCookieOptions.For(builder.Environment));
        builder.Services.AddSingleton(services => PageBrowserCookieOptions.For(
            builder.Environment, services.GetRequiredService<PageStoresOptions>().Visitor));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(services => new PageEnvironment(
              services.GetRequiredService<ReservedPaths>(),
              services.GetRequiredService<PageEditorOptions>(),
              services.GetRequiredService<PageLayoutOptions>(),
              services.GetRequiredService<RenderOptions>(),
              services.GetRequiredService<PageContentTypes>(),
              services.GetRequiredService<PageStoresOptions>(),
              services.GetRequiredService<PageFetchOptions>(),
              services.GetRequiredService<PageReadOptions>(),
              services.GetRequiredService<PageSourceScan>(),
              services.GetRequiredService<IStoreValues>(),
              services.GetRequiredService<IStoreMessages>(),
              services.GetRequiredService<IStoreObjects>(),
              services.GetRequiredService<IPageReadStore>(),
              services.GetRequiredService<PageSeeds>(),
              services.GetRequiredService<PageSaver>(),
              services.GetRequiredService<IPageFetcher>(),
              services.GetRequiredService<PageMetrics>(),
              services.GetRequiredService<IAntiforgery>(),
              services.GetRequiredService<MarkdownRenderer>(),
              services.GetRequiredService<DiffRenderer>(),
              services.GetRequiredService<IPageSessionStore>(),
              services.GetRequiredService<PageSessionCookieOptions>(),
              services.GetRequiredService<PageBrowserCookieOptions>(),
              services.GetRequiredService<TimeProvider>(),
              services.GetRequiredService<PageJobs>(),
              services.GetRequiredService<PageChecks>(),
              services.GetRequiredService<PageJobAuthorityFactory>(),
              services.GetRequiredService<IRecordCatalog>()));
        builder.Services.AddSingleton(services => new PageHealth(services.GetRequiredService<RenderOptions>().AbandonedLimit));

        builder.Services.AddSingleton<AssistantMetrics>();
        builder.Services.AddSingleton<ModelWindowSource>();
        builder.Services.AddSingleton<ModelWindowSampler>();
        builder.Services.AddSingleton<ChartRenderer>();
        builder.Services.AddSingleton<Dashboard>();
        builder.Services.AddSingleton<DiffRenderer>();
        builder.Services.AddSingleton<IAssistantFetcher, AssistantFetcher>();
        builder.Services.AddSingleton<IPageCompiler, PageCompiler>();
        builder.Services.AddSingleton<IPageFetcher, PageFetcher>();
        builder.Services.AddSingleton<IPageReadStore, SqlitePageReadStore>();
        builder.Services.AddSingleton<IPageCommitStore, SqlitePageCommitStore>();
        builder.Services.AddSingleton<IPageRevisionStore, SqlitePageRevisionStore>();
        builder.Services.AddSingleton<IPageSessionStore, SqlitePageSessionStore>();
        builder.Services.AddSingleton<IStoreValues, SqliteStoreValues>();
        builder.Services.AddSingleton<IStoreMessages, SqliteStoreMessages>();
        builder.Services.AddSingleton<IStoreObjects, SqliteStoreObjects>();
        builder.Services.AddSingleton<IPasswordService, PasswordService>();
        builder.Services.AddSingleton<IUserConsole, SystemUserConsole>();
        builder.Services.AddSingleton<ImportQueue>();
        builder.Services.AddSingleton<ImportStageStore>();
        builder.Services.AddSingleton<MarkdownRenderer>();
        // Reads the licence and notice files the image carries, once, when the page first asks.
        builder.Services.AddSingleton(new LicenceLibrary(AppContext.BaseDirectory));
        builder.Services.AddSingleton<OutboundFetcher>();
        builder.Services.AddSingleton<PageBudget>();
        builder.Services.AddSingleton<PageImageBuilder>();
        builder.Services.AddSingleton<PageJobQueue>();
        builder.Services.AddSingleton<PageJobs>();
        builder.Services.AddSingleton<PageJobAuthorityVerifier>();
        builder.Services.AddSingleton<PageJobAuthorityFactory>();
        builder.Services.AddSingleton<IWorkerStore, ScopedWorkerStore>();
        builder.Services.AddSingleton(services => new PageJobEnvironment(
            services.GetRequiredService<PageJobOptions>(),
            services.GetRequiredService<IWorkerStore>(),
            services.GetRequiredService<IStoreValues>(),
            services.GetRequiredService<IStoreMessages>(),
            services.GetRequiredService<IStoreObjects>(),
            services.GetRequiredService<PageStoresOptions>(),
            services.GetRequiredService<IPageSessionStore>(),
            services.GetRequiredService<IPageFetcher>(),
            services.GetRequiredService<PageFetchOptions>(),
            services.GetRequiredService<IPageReadStore>(),
            services.GetRequiredService<PageSeeds>(),
            services.GetRequiredService<PageReadOptions>(),
            services.GetRequiredService<PageSourceScan>(),
            services.GetRequiredService<ReservedPaths>(),
            services.GetRequiredService<PageContentTypes>(),
            services.GetRequiredService<PageImageBuilder>(),
            services.GetRequiredService<PageLibraries>(),
            services.GetRequiredService<IPageCompiler>(),
            services.GetRequiredService<PageMetrics>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            render,
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<IRecordCatalog>()));
        builder.Services.AddSingleton<PageLibraries>();
        builder.Services.AddSingleton<PageMetrics>();
        builder.Services.AddSingleton<PagePathPolicy>();
        builder.Services.AddSingleton<PageSourceScan>();
        builder.Services.AddSingleton(new PageSeeds(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds")));
        builder.Services.AddSingleton<PageSaver>();
        builder.Services.AddSingleton<PageChecks>();
        builder.Services.AddSingleton<PageStorageRefresh>();
        builder.Services.AddSingleton<PageToolExecutor>();
        // Reads the XML documentation this build carries, once, when a page or the tool first asks.
        builder.Services.AddSingleton(services => new ReferenceLibrary(
            AppContext.BaseDirectory,
            services.GetRequiredService<ILogger<ReferenceLibrary>>()));
        builder.Services.AddSingleton<RunQueue>();
        builder.Services.AddSingleton<RunRegistry>();
        builder.Services.AddSingleton<OwnerInterruptCoordinator>();
        builder.Services.AddSingleton<ViewTokenStore>();

        builder.Services.AddScoped<IConversationStore, SqliteConversationStore>();
        // Registered as a scoped store so `users remove` can sweep an account's memories with the
        // rest of its rows.
        builder.Services.AddScoped<IMemoryStore, SqliteMemoryStore>();
        // A declaration is applied inside the save that carries it, so the applier is what both write
        // seams are given: the one a single save uses and the one a page's transaction commits through.
        builder.Services.AddSingleton<IRecordCatalog>(services => new SqliteRecordCatalog(
            services.GetRequiredService<IDbContextFactory<StorageDbContext>>()));
        builder.Services.AddSingleton(services => new DeclaredTableApplier(
            services.GetRequiredService<PageStoresOptions>().Records));

        // The listing judges an image's state against the running build, so the store is told which
        // one that is; a page whose image came from an earlier build says Stale until it is requested.
        builder.Services.AddScoped<IPageStore>(services => new SqlitePageStore(
            services.GetRequiredService<StorageDbContext>(),
            services.GetRequiredService<ILogger<SqlitePageStore>>(),
            services.GetRequiredService<PageStorageRefresh>(),
            services.GetRequiredService<IPageCompiler>().RuntimeId,
            services.GetRequiredService<DeclaredTableApplier>()));

        builder.Services.AddScoped<IPageBatchStore, SqlitePageBatchStore>();
        builder.Services.AddScoped<IUserStore, SqliteUserStore>();
        builder.Services.AddScoped<ImportProcessor>();
        builder.Services.AddScoped<PageArchiveReader>();
        builder.Services.AddScoped<PageEditor>();
        builder.Services.AddScoped<PageRenderer>();
        builder.Services.AddScoped<RunProcessor>();

        builder.Services.AddSingleton<IMetricsSource>(services => {
            // The per-query deadline belongs to the source: one slow query must not hold the page.
            return new PrometheusSource(
                new HttpClient {
                    BaseAddress = metrics.PrometheusUrl,
                    Timeout = Timeout.InfiniteTimeSpan,
                },
                services.GetRequiredService<ILogger<PrometheusSource>>(),
                metrics.QueryTimeout);
        });
        // Metrics only: registering no instrumentation package means no tracer or logger provider.
        // The exporter serves /metrics on this listener; Caddy refuses that path publicly.
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
            // Try to keep these sorted alpha
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Runtime")
            .AddMeter(AssistantMetrics.MeterName)
            .AddMeter(PageMetrics.MeterName)
            .AddView("assistant.context.estimate.error", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.EstimateErrorBuckets })
            .AddView("assistant.memory.chars", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.MemoryCharBuckets })
            .AddView("assistant.memory.recalled", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.MemoryCountBuckets })
            .AddView("assistant.provider.first_token.duration", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.ProviderBuckets })
            .AddView("assistant.provider.request.duration", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.ProviderBuckets })
            .AddView("assistant.run.duration", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.RunDurationBuckets })
            .AddView("assistant.run.steps", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.StepBuckets })
            .AddView("assistant.run.tool_calls", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.ToolCallBuckets })
            .AddView("assistant.tool.duration", new ExplicitBucketHistogramConfiguration { Boundaries = AssistantMetrics.ToolDurationBuckets })
            .AddView("page.compile.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.fetch.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.image.size", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.ImageBuckets })
            .AddView("page.import.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.invoke.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.load.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.render.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.render.wait", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.search.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SearchBuckets })
            .AddView("page.save.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.SecondsBuckets })
            .AddView("page.stream.duration", new ExplicitBucketHistogramConfiguration { Boundaries = PageMetrics.StreamBuckets })
            .AddPrometheusExporter());

        WebApplication app = builder.Build();

        // run pending database migrations
        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope()) {
            StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
            await database.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
            // WAL before anything else serves: a reader must never be able to fail a page's commit.
            await SqliteJournalMode.EnsureWriteAheadLogAsync(database, app.Lifetime.ApplicationStopping);
            // A restart interrupts a provider request rather than resuming it, so the runs that were
            // in flight are closed out before anything can answer for them.
            IConversationStore assistantConversations = scope.ServiceProvider.GetRequiredService<IConversationStore>();
            await assistantConversations.RecoverInterruptedRunsAsync(app.Lifetime.ApplicationStopping);
            int repaired = await PageContentHashRepair.RunAsync(database, app.Lifetime.ApplicationStopping);
            if (repaired > 0) {
                app.Logger.LogInformation(
                    "Repaired the content hash of {Pages} page(s) written before seed provenance", repaired);
            }

            IPageStore pages = scope.ServiceProvider.GetRequiredService<IPageStore>();
            await PageAssetSeeder.SeedAsync(
                pages, scope.ServiceProvider.GetRequiredService<PageSeeds>(), app.Lifetime.ApplicationStopping);

            // The seed update that needs no choice is finished here rather than waiting for the owner
            // to open the seed page. A command is not a start that serves: `seeds pull` exists to take
            // the store's bytes into the working tree, so writing the release's seed over the row it
            // was most likely asked for first would empty it.
            if (!isCommand) {
                await PageSeedApplier.ApplyAsync(
                    scope.ServiceProvider.GetRequiredService<PageEditor>(),
                    scope.ServiceProvider.GetRequiredService<PageSeeds>(),
                    app.Logger,
                    app.Lifetime.ApplicationStopping);
            }

            // A stored page at a key the shell answers can never be served. It is what a seed that
            // became a shell file leaves behind, and the editor cannot remove it; point the owner at
            // the one page that can. The scan reports and never writes, and it is the web host's:
            // a command's stdout is its result, and the warning would land in it.
            if (!isCommand) {
                IReadOnlyList<PagePath> strays = await ReservedPageScan.FindAsync(
                    pages, reserved, app.Lifetime.ApplicationStopping);
                if (strays.Count > 0) {
                    app.Logger.LogWarning(
                        "Stored pages are at {Count} key(s) the shell answers and cannot serve: {Paths}. Open {Path} to review and scrub them.",
                        strays.Count,
                        string.Join(", ", strays.Select(path => path.Key)),
                        SeedsEndpoint.Path);
                }
            }
        }

        if (isCommand) {
            await using (app) {
                if (isSeedsCommand) {
                    using Stream output = Console.OpenStandardOutput();
                    Environment.ExitCode = await SeedCommands.RunAsync(
                        args[1..], app.Services, output, Console.Error, app.Lifetime.ApplicationStopping);
                } else {
                    Environment.ExitCode = await UserCommands.RunAsync(
                        args[1..], app.Services, app.Lifetime.ApplicationStopping);
                }

                return;
            }
        }

        // The compiler's reference set comes from the runtime, so resolve it before the host listens:
        // a runtime that cannot supply one must fail the deploy, not the first page that gets saved.
        _ = app.Services.GetRequiredService<IPageCompiler>();

        // The model's window is where the assistant's whole budget comes from, and it is read before
        // anything can run. A provider that does not answer leaves the budget unset and every page
        // still serves; a provider that answers without the configured model stops the deploy, because
        // that answer is authoritative and the owner should hear it now rather than from a failed run.
        await app.Services.GetRequiredService<ModelWindowSampler>()
            .RefreshAsync(app.Lifetime.ApplicationStopping);

        // The map is read and validated above with the rest of configuration; the journal line that
        // says what pages will serve is written now that the logger exists.
        PageContentTypesConfiguration.Log(
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Pages.ContentTypes"), contentTypes);

        // Before anything that reads the scheme. Without it every request arrives as plain HTTP from
        // the proxy, so HSTS emits nothing and IsHttps is always false.
        app.UseForwardedHeaders();

        // The probe and the scrape are the machine asking whether the site is up, not a visit;
        // leaving them in the request duration histogram makes the panels a reading of the poller.
        app.Use(MachineTrafficMetrics.SkipAsync);

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

        // Inside the exception handler, so a response that has already started is not reported as an
        // unhandled exception when the client leaves mid-write (ARCH-13, BUG-17).
        app.UseDisconnectGuard();

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
                    ? ErrorMessage.FormExpired(isAuthenticated, EditorPaths.Of(failed.RequestServices))
                    : ErrorMessage.ForStatus(failed.Response.StatusCode, isAuthenticated, EditorPaths.Of(failed.RequestServices));

            await ErrorResponses.WriteAsync(failed, failed.Response.StatusCode, message);
        });

        // The shell's own files - the administration sheets and script, the status sheet, the
        // reference sheet and the icon - are the web root's, not the page store's. Ahead of routing,
        // so a physical file answers before the page catch-all can claim its key; the security
        // headers above still run, and a stored asset keeps its own versioned link.
        app.UseStaticFiles();

        // Order matters: routing first, so the limiter and identity read endpoint metadata, then
        // authorization, then the antiforgery check that guards the unsafe ones.
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        // After authentication and routing: the account and the route's ids are the two facts the
        // scope could not carry when it opened.
        app.UseRequestLogSubject();
        // Before every refusal, so one line says a request ended unauthenticated however it was
        // answered - the view guard's 401, the cookie's challenge, a page's own sign-in redirect.
        // The host's ban reads that line; deploy/fail2ban/ is where the words are matched.
        app.Use(AuthenticationFailures.ApplyAsync);
        // A view token reads and never writes, and a bearer header that did not authenticate is a 401.
        app.Use(ViewGuard.ApplyAsync);
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapGet("/admin/status", StatusEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapGet("/admin/status/chart", ChartEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name);

        app.MapGet("/admin", AdminIndexEndpoint.HandleAsync);

        app.MapGet("/admin/scrub/{**path}", ScrubEndpoint.ConfirmAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapPost("/admin/scrub/{**path}", ScrubEndpoint.ScrubAsync)
            .RequireAuthorization(EditorPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        app.MapGet("/admin/changes", ChangesEndpoint.HandleAsync)
            .RequireAuthorization(EditorPolicy.Name);
        // Public despite the key: the reference describes the build serving the request, as /about
        // does, and the repository is published to be read.
        app.MapGet("/admin/reference", ReferenceEndpoint.IndexAsync);
        app.MapGet("/admin/reference/{slug}", ReferenceEndpoint.AssemblyAsync);
        app.MapGet("/admin/reference/{slug}/{namespace}", ReferenceEndpoint.NamespaceAsync);
        // Public for the same reason as the reference: the licence of published source is for
        // whoever asks, and the notices are what the image ships.
        app.MapGet("/admin/copyright", CopyrightEndpoint.HandleAsync);
        app.MapGet("/admin/seeds", SeedsEndpoint.GetAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapPost("/admin/seeds", SeedsEndpoint.PostAsync)
            .RequireAuthorization(EditorPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
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

        // A stored object, by digest. The route streams the bytes from SQLite and is answered before
        // the page catch-all, so an image works even when the page pipeline does not.
        app.MapMethods("/o/{sha}", ["GET", "HEAD"], ObjectEndpoint.HandleAsync);

        app.MapHealthChecks(MachineTrafficMetrics.HealthPath, new HealthCheckOptions {
            ResponseWriter = ReportWriter.WriteAsync,
        });

        app.MapGet("/assistant", AssistantEndpoint.ListAsync)
            .RequireAuthorization(EditorPolicy.Name);
        app.MapPost("/assistant", AssistantEndpoint.ListAsync)
            .RequireAuthorization(EditorPolicy.Name)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        app.MapGet("/assistant/{id:long}", AssistantConversationPage.ConversationAsync)
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
        app.MapPost("/assistant/{id:long}/runs/{runId}/interrupt", AssistantEndpoint.InterruptAsync)
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
        // The scrape target is Prometheus inside the podman network; /metrics is reserved so a page
        // cannot claim the key, and Caddy answers 404 for it on the public origin.
        app.MapPrometheusScrapingEndpoint(MachineTrafficMetrics.ScrapePath);
        // Pages run for GET, POST and HEAD only; anything else is answered 405 by routing rather than
        // by page code that never expected it.
        app.MapMethods("{**path}", ["GET", "POST", "HEAD"], PageEndpoint.HandleAsync);

        // One line so a restart is visible without reading the framework's startup chatter.
        ILogger startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
        startup.LogInformation("uk.osric.asp starting in {EnvironmentName}, viewing the site at {Origin}",
            app.Environment.EnvironmentName, site.Origin);

        // The assistant's own view fetches this origin from inside the process. A host bound anywhere
        // else (make run's ASPNETCORE_URLS, a changed Site:Origin) answers the tool a network failure
        // rather than a page, so the mismatch is worth one line at startup rather than a mystery later.
        if (app.Environment.IsProduction()
            && !string.Equals(site.Origin.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)) {
            // Found by the first view of the editor: in Production the antiforgery cookie is
            // Secure-always, so a page that mints a token throws when the request is plaintext. The
            // view still reads every page that does not, which is why this is a warning rather than a
            // refusal: the value is usable, and the site's https origin is the one that shows the
            // pages a visitor actually gets.
            startup.LogWarning(
                "Site:Origin is {Origin}, which is plaintext, so a view of a page that mints an antiforgery token (the editor) will fail. Name the site's https origin instead.",
                site.Origin);
        }

        await app.RunAsync();
    }
}
