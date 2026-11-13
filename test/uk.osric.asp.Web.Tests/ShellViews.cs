// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using System.Security.Claims;
using uk.osric.asp.Core.Charting;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Metrics;
using uk.osric.asp.Web.Pages;

/// <summary>Renders each document the shell serves, once, for the shared accessibility rules.</summary>
/// <remarks>
/// Every view goes through the endpoint that writes it, with the stand-ins the endpoint fixtures use,
/// so the rules see the markup an installation answers with rather than a hand-built copy of it.
/// </remarks>
internal static class ShellViews {

    private const string Source = "<% ctx.Write(\"hello\"); %>";

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);

    /// <summary>The views, in the order a reader meets them: sign in, then the rest.</summary>
    public static IReadOnlyList<string> Names { get; } = [
        "login",
        "login-failed",
        "logout",
        "pages-listing",
        "pages-editor",
        "scrub-confirmation",
        "status",
        "error",
        "error-details",
        "not-found",
        "compile-error",
        "assistant-list",
        "assistant-chat",
        "assistant-chat-reasoning",
        "assistant-delete",
        "assistant-memory",
        "assistant-memory-forget",
        "admin-changes",
        "admin-restore",
        "admin-restore-result",
        "admin-export",
        "admin-import",
        "admin-import-failed",
        "admin-import-review",
        "admin-import-progress",
        "admin",
        "help",
    ];

    /// <summary>Renders one of <see cref="Names"/>.</summary>
    /// <param name="name">The view to render.</param>
    public static Task<ShellView> RenderAsync(string name) => name switch {
        "login" => RenderAsync(name, LoginAsync),
        "login-failed" => RenderAsync(name, FailedLoginAsync),
        "logout" => RenderAsync(name, LogoutAsync),
        "pages-listing" => RenderAsync(name, () => PagesAsync(editor: false)),
        "pages-editor" => RenderAsync(name, () => PagesAsync(editor: true)),
        "scrub-confirmation" => RenderAsync(name, ScrubConfirmationAsync),
        "status" => RenderAsync(name, StatusAsync),
        "error" => RenderAsync(name, () => ErrorAsync(withDetails: false)),
        "error-details" => RenderAsync(name, () => ErrorAsync(withDetails: true)),
        "not-found" => RenderAsync(name, NotFoundAsync),
        "compile-error" => RenderAsync(name, CompileErrorAsync),
        "assistant-list" => RenderAsync(name, AssistantListAsync),
        "assistant-chat" => RenderAsync(name, () => AssistantChatAsync(withReasoning: false)),
        "assistant-chat-reasoning" => RenderAsync(name, () => AssistantChatAsync(withReasoning: true)),
        "assistant-delete" => RenderAsync(name, AssistantDeleteAsync),
        "assistant-memory" => RenderAsync(name, AssistantMemoryAsync),
        "assistant-memory-forget" => RenderAsync(name, AssistantMemoryForgetAsync),
        "admin-changes" => RenderAsync(name, AdminChangesAsync),
        "admin-restore" => RenderAsync(name, AdminRestoreAsync),
        "admin-restore-result" => RenderAsync(name, AdminRestoreResultAsync),
        "admin-export" => RenderAsync(name, AdminExportAsync),
        "admin-import" => RenderAsync(name, AdminImportAsync),
        "admin-import-failed" => RenderAsync(name, AdminImportFailedAsync),
        "admin-import-review" => RenderAsync(name, AdminImportReviewAsync),
        "admin-import-progress" => RenderAsync(name, AdminImportProgressAsync),
        "admin" => RenderAsync(name, AdminAsync),
        "help" => RenderAsync(name, HelpAsync),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such shell view."),
    };

    private static async Task<ShellView> RenderAsync(string name, Func<Task<string>> render) =>
        new(name, await render());

    private static async Task<string> LoginAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/login");

        await LoginEndpoint.GetAsync(context, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> LogoutAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/logout");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));

        await LoginEndpoint.LogoutConfirmationAsync(context, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    // A tombstone, because the confirmation refuses to scrub a live page.
    private static async Task<string> ScrubConfirmationAsync() {
        FakePageStore storage = new() {
            ["/gone"] = new PageTemplate(PageHarness.Key("/gone"), Source),
        };
        await storage.DeletePageAsync(PageHarness.Key("/gone"));

        using PageHarness pages = new(storage);
        DefaultHttpContext context = TestHttp.CreateContext("gone", "GET", "/admin/scrub/gone");

        await ScrubEndpoint.ConfirmAsync(context, pages.Editor, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    // The administration index and the help page are stored pages, so they are rendered the way the
    // host renders one: the seeded fragment wrapped in the seeded layout.
    private static Task<string> AdminAsync() => StoredPageAsync("/admin");

    private static Task<string> HelpAsync() => StoredPageAsync("/help");

    // The editor page is a stored page too, so the listing and the editor are rendered through the
    // page pipeline. It needs pages to list and a page to edit, in both stores: the page reads the
    // catalog, and the editor's hash comes from the same read.
    private static Task<string> PagesAsync(bool editor) {
        (FakePageStore store, FakePageReadStore read) = PagesForEditor();
        return StoredPageAsync("/pages", editor ? "?edit=%2Fabout" : string.Empty, store, read);
    }

    private static (FakePageStore Store, FakePageReadStore Read) PagesForEditor() {
        FakePageStore store = new();
        FakePageReadStore read = new();
        foreach (string key in new[] { "/about", "/blog" }) {
            string template = "<p>" + key + "</p>";
            store[key] = new PageTemplate(PageHarness.Key(key), template);
            read.Save(key, template, hash: store[key]!.Hash);
        }

        return (store, read);
    }

    private static async Task<string> StoredPageAsync(
        string path,
        string query = "",
        FakePageStore? stored = null,
        FakePageReadStore? readStore = null) {
        FakePageStore pages = stored ?? new();
        foreach (string seed in new[] { "/_layout", path }) {
            pages[seed] = new PageTemplate(
                PageHarness.Key(seed),
                await PageAssetSeeder.ReadSeedTemplateAsync(PageHarness.Key(seed)) ?? string.Empty);
        }

        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), "GET", path);
        if (query.Length > 0) {
            context.Request.QueryString = new QueryString(query);
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));

        using PageHarness harness = new(pages);
        using PageMetrics metrics = new();

        await PageEndpoint.HandleAsync(
            context,
            harness.Renderer,
            NullLoggerFactory.Instance,
            new PageEnvironment(
                Reserved,
                new PageEditorOptions(),
                new PageLayoutConfiguration { Default = "/_layout" },
                new RenderOptions(),
                TestContentTypes.Shipped,
                new PageStorageOptions(),
                new PageFetchOptions(),
                new PageReadOptions(),
                new FakePageStateStore(),
                readStore ?? new FakePageReadStore(),
                harness.Saver,
                new FakePageFetcher(),
                metrics,
                new TestAntiforgery(),
                new MarkdownRenderer(),
                new DiffRenderer(),
                new PageSessionOptions(),
                new FakePageSessionStore(),
                TestSessions.Cookie),
            harness.Editor);

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> FailedLoginAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/login");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> {
            ["username"] = "owner",
            ["password"] = "wrong",
        });

        await LoginEndpoint.PostAsync(
            context,
            new FakeUserStore(),
            new FakePasswordService(),
            new TestAntiforgery(),
            new RecordingAuthenticationService(),
            new FakePageSessionStore(),
            TestSessions.Cookie,
            new RecordingLoggerFactory());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> StatusAsync() {
        MetricsOptions options = new() {
            PrometheusUrl = new Uri("http://prometheus:9090"),
            TimeZone = TimeZoneInfo.Utc,
        };
        Dashboard dashboard = new(
            new ChartRenderer(new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("ok", [
                    new ChartPoint(DateTimeOffset.Parse("2026-11-02T05:30:00Z", CultureInfo.InvariantCulture), 1),
                ]),
            ])), options),
            new TestClock(),
            NullLogger<Dashboard>.Instance);
        DefaultHttpContext context = TestHttp.CreateContext(null);

        await StatusEndpoint.HandleAsync(context, dashboard, new TestClock(), CancellationToken.None);

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> NotFoundAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("missing", "GET", "/missing");

        await NotFound.WriteAsync(context, PagePath.Normalize("/missing"));

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> ErrorAsync(bool withDetails) {
        DefaultHttpContext context = TestHttp.CreateContext(null);
        context.TraceIdentifier = "trace";
        context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature {
            Error = new InvalidOperationException("boom"),
            Path = "/boom",
        });

        if (withDetails) {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "owner")], "test"));
        }

        await ErrorEndpoint.HandleAsync(context);

        return await TestHttp.ReadBodyAsync(context);
    }

    // The stored compilation is handed to the endpoint rather than compiled, so the failure is the
    // one this test chose and no Roslyn runs to build it.
    private static async Task<string> CompileErrorAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        storage["/broken"] = new PageTemplate(PageHarness.Key("/broken"), BrokenSource, Broken(pages.Compiler.RuntimeId));
        DefaultHttpContext context = TestHttp.CreateContext("broken", "GET", "/broken");

        await PageEndpoint.HandleAsync(
            context,
            pages.Renderer,
            NullLoggerFactory.Instance,
            new PageEnvironment(
                Reserved,
                new PageEditorOptions(),
                new PageLayoutConfiguration(),
                new RenderOptions(),
                TestContentTypes.Shipped,
                new PageStorageOptions(),
                new PageFetchOptions(),
                new PageReadOptions(),
                new FakePageStateStore(),
                new FakePageReadStore(),
                pages.Saver,
                new FakePageFetcher(),
                new PageMetrics(),
                new TestAntiforgery(),
                new MarkdownRenderer(),
                new DiffRenderer(),
                new PageSessionOptions(),
                new FakePageSessionStore(),
                TestSessions.Cookie));

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AssistantListAsync() {
        DefaultHttpContext context = AuthenticatedContext("/assistant");
        await AssistantEndpoint.ListAsync(context, new ShellConversationStore(), new TestAntiforgery(),
            Microsoft.Extensions.Options.Options.Create(new AssistantOptions { Enabled = true }));
        return await TestHttp.ReadBodyAsync(context);
    }

    // The reasoning view also carries a finished tool call, so the shared rules see the tool
    // details the chat renders from stored messages: arguments in the summary, both halves inside.
    // A successful write finishes the turn, so the rules see the changed-pages card as well.
    private static async Task<string> AssistantChatAsync(bool withReasoning) {
        DefaultHttpContext context = AuthenticatedContext("/assistant/1");
        ShellConversationStore conversations = new(withReasoning
            ? [
                new Message {
                    ConversationId = 1,
                    RunId = "run-1",
                    Sequence = 1,
                    Role = "assistant",
                    Content = "## Answer\n\nA **table**:\n\n| Name | Value |\n| --- | --- |\n| one | 1 |",
                    Reasoning = "Reasoning",
                    ToolCallsJson = """[{"id":"call-1","type":"function","function":{"name":"read_page","arguments":"{\"path\":\"/blog\"}"}}]""",
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                new Message {
                    ConversationId = 1,
                    RunId = "run-1",
                    Sequence = 2,
                    Role = "tool",
                    Content = """{"ok":true,"data":null}""",
                    ToolCallId = "call-1",
                    ToolName = "read_page",
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                new Message {
                    ConversationId = 1,
                    RunId = "run-1",
                    Sequence = 3,
                    Role = "tool",
                    Content = """{"ok":true,"data":{"path":"/blog","revisionHash":"abc"},"error":null}""",
                    ToolCallId = "call-2",
                    ToolName = "write_page",
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            ]
            : []);
        await AssistantEndpoint.ConversationAsync(context, 1, conversations, new TestAntiforgery(),
            new RunRegistry(new AssistantOptions(), TimeProvider.System), new AssistantOptions(),
            new StubAccountBalanceClient(), new MarkdownRenderer());
        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AssistantDeleteAsync() {
        DefaultHttpContext context = AuthenticatedContext("/assistant/1/delete");
        await AssistantEndpoint.DeleteConfirmationAsync(context, 1, new ShellConversationStore(), new TestAntiforgery());
        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AssistantMemoryAsync() {
        DefaultHttpContext context = AuthenticatedContext(AssistantMemoryEndpoint.Path);
        await AssistantMemoryEndpoint.ListAsync(context, new ShellMemoryStore(), new TestAntiforgery(),
            new AssistantOptions { MaxMemoriesPerOwner = 200, MaxMemoryChars = 1_000 });
        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AssistantMemoryForgetAsync() {
        DefaultHttpContext context = AuthenticatedContext(AssistantMemoryEndpoint.Path + "/1/forget");
        await AssistantMemoryEndpoint.ForgetConfirmationAsync(context, 1, new ShellMemoryStore(), new TestAntiforgery());
        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AdminChangesAsync() {
        string file = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.changes.{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "0123456789abcdef0123456789abcdef01234567 A commit\n");

        try {
            DefaultHttpContext context = AuthenticatedContext("/admin/changes");
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Changelist"] = file })
                .Build();

            await ChangesEndpoint.HandleAsync(context, configuration);

            return await TestHttp.ReadBodyAsync(context);
        } finally {
            File.Delete(file);
        }
    }

    private static async Task<string> AdminRestoreAsync() {
        using PageHarness pages = await RestoreHarnessAsync();
        DefaultHttpContext context = AuthenticatedContext("/admin/restore");

        await RestoreEndpoint.PreviewAsync(context, pages.Editor, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AdminRestoreResultAsync() {
        using PageHarness pages = await RestoreHarnessAsync();
        DefaultHttpContext context = AuthenticatedContext("/admin/restore");

        await RestoreEndpoint.RestoreAsync(context, pages.Editor, NullLoggerFactory.Instance);

        return await TestHttp.ReadBodyAsync(context);
    }

    // Every seed at its bundled source but one, so the preview has a change to show and the result
    // has something it actually wrote.
    private static async Task<PageHarness> RestoreHarnessAsync() {
        FakePageStore store = new();
        foreach (PagePath path in PageAssetSeeder.SeedPaths()) {
            store[path.Key] = new PageTemplate(path, (await PageAssetSeeder.ReadSeedTemplateAsync(path))!);
        }

        store["/about"] = new PageTemplate(PageHarness.Key("/about"), "changed by the owner");
        return new PageHarness(store);
    }

    private static async Task<string> AdminExportAsync() {
        DefaultHttpContext context = AuthenticatedContext("/admin/export");
        FakePageStore store = new() {
            ["/page"] = new PageTemplate(PageHarness.Key("/page"), Source),
            ["/notes"] = new PageTemplate(PageHarness.Key("/notes"), Source),
        };

        await ExportEndpoint.ListAsync(context, store, Reserved, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AdminImportAsync() {
        DefaultHttpContext context = AuthenticatedContext("/admin/import");

        await ImportEndpoint.UploadFormAsync(context, new TestAntiforgery(), new ArchiveOptions());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AdminImportFailedAsync() {
        DefaultHttpContext context = AuthenticatedContext("/admin/import");
        context.Request.Method = "POST";
        context.Request.ContentType = "multipart/form-data";

        FormFileCollection files = [new FormFile(new MemoryStream("not a database"u8.ToArray()), 0, 15, "archive", "pages.sqlite")];
        Dictionary<string, StringValues> noFields = [];
        context.Request.Form = new FormCollection(noFields, files);

        await ImportEndpoint.UploadAsync(
            context,
            new PageArchiveReader(new ArchiveOptions(), new PageStorageOptions(), Reserved, NullLogger<PageArchiveReader>.Instance),
            new ImportStageStore(new ArchiveOptions(), new TestClock()),
            new ArchiveOptions(),
            NullLoggerFactory.Instance);

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AdminImportReviewAsync() {
        DefaultHttpContext context = AuthenticatedContext("/admin/import/0123456789abcdef0123456789abcdef");
        context.Request.RouteValues["id"] = "0123456789abcdef0123456789abcdef";
        ImportStageStore stages = new(new ArchiveOptions(), new TestClock());
        stages.Create([
            new PageArchiveEntry(PageHarness.Key("/page"), Source, [new("visits", "1")]),
            new PageArchiveEntry(PageHarness.Key("/notes"), Source, []),
        ]);

        await ImportEndpoint.ReviewAsync(context, stages, new FakePageStore(), new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AdminImportProgressAsync() {
        DefaultHttpContext context = AuthenticatedContext("/admin/import/0123456789abcdef0123456789abcdef/progress");
        context.Request.RouteValues["id"] = "0123456789abcdef0123456789abcdef";
        ImportStageStore stages = new(new ArchiveOptions(), new TestClock());
        ImportStage stage = stages.Create([new PageArchiveEntry(PageHarness.Key("/page"), Source, [])]);
        stage.TryApprove([PageHarness.Key("/page")]);

        await ImportEndpoint.ProgressAsync(context, stages);

        return await TestHttp.ReadBodyAsync(context);
    }

    private static DefaultHttpContext AuthenticatedContext(string path) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", path);
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
        return context;
    }

    private const string BrokenSource = "<% int value = ; %>";

    private static PageImage Broken(string runtimeId) => new(
        TemplateFingerprint.Of(BrokenSource),
        runtimeId,
        null,
        null,
        [new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 16), "CS1525: Invalid expression term ';'")]);
}
