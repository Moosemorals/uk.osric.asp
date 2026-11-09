// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
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

    /// <summary>The views, in the order a reader meets them: sign in, the editor, then the rest.</summary>
    public static IReadOnlyList<string> Names { get; } = [
        "login",
        "login-failed",
        "logout",
        "listing",
        "editor",
        "editor-with-diagnostics",
        "history",
        "revision",
        "delete-confirmation",
        "status",
        "error",
        "error-details",
        "not-found",
        "compile-error",
        "assistant-list",
        "assistant-chat",
        "assistant-chat-reasoning",
        "assistant-delete",
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
        "listing" => RenderAsync(name, ListingAsync),
        "editor" => RenderAsync(name, () => EditorAsync(NewStore(), path: "/page")),
        "editor-with-diagnostics" => RenderAsync(name, EditorWithDiagnosticsAsync),
        "history" => RenderAsync(name, () => EditorAsync(NewStore(), "/page", "cmd=history")),
        "revision" => RenderAsync(name, RevisionAsync),
        "delete-confirmation" => RenderAsync(name, () => EditorAsync(NewStore(), "/page", "cmd=delete")),
        "status" => RenderAsync(name, StatusAsync),
        "error" => RenderAsync(name, () => ErrorAsync(withDetails: false)),
        "error-details" => RenderAsync(name, () => ErrorAsync(withDetails: true)),
        "not-found" => RenderAsync(name, NotFoundAsync),
        "compile-error" => RenderAsync(name, CompileErrorAsync),
        "assistant-list" => RenderAsync(name, AssistantListAsync),
        "assistant-chat" => RenderAsync(name, () => AssistantChatAsync(withReasoning: false)),
        "assistant-chat-reasoning" => RenderAsync(name, () => AssistantChatAsync(withReasoning: true)),
        "assistant-delete" => RenderAsync(name, AssistantDeleteAsync),
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

    // The administration index and the help page are stored pages, so they are rendered the way the
    // host renders one: the seeded fragment wrapped in the seeded layout.
    private static Task<string> AdminAsync() => StoredPageAsync("/admin");

    private static Task<string> HelpAsync() => StoredPageAsync("/help");

    private static async Task<string> StoredPageAsync(string path) {
        FakePageStore pages = new();
        foreach (string seed in new[] { "/_layout", path }) {
            pages[seed] = new PageTemplate(
                PageHarness.Key(seed),
                await PageAssetSeeder.ReadSeedTemplateAsync(PageHarness.Key(seed)) ?? string.Empty);
        }

        DefaultHttpContext context = TestHttp.CreateContext(path.TrimStart('/'), "GET", path);
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
                new PageLayoutConfiguration { Default = "/_layout" },
                new RenderOptions(),
                TestContentTypes.Shipped,
                new PageStorageOptions(),
                new PageFetchOptions(),
                new PageReadOptions(),
                new FakePageStateStore(),
                new FakePageReadStore(),
                new FakePageFetcher(),
                metrics,
                new TestAntiforgery(),
                new MarkdownRenderer()));

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
            new RecordingLoggerFactory());

        return await TestHttp.ReadBodyAsync(context);
    }

    // A live page with an action of each kind, a page that compiled with warnings, and a tombstone,
    // so the listing shows every state the table can say as well as both action sets.
    private static async Task<string> ListingAsync() {
        FakePageStore storage = NewStore();
        storage["/blog"] = new PageTemplate(PageHarness.Key("/blog"), Source, new PageImage(
            TemplateFingerprint.Of(Source),
            "1:3:elsewhere",
            [1],
            [2],
            [new PageDiagnostic(PageDiagnosticSeverity.Warning, new TemplatePosition(1, 4), "CS0219: unused variable")]));
        await storage.DeletePageAsync(PageHarness.Key("/notes"));

        return await EditorAsync(storage, path: null);
    }

    private static Task<string> RevisionAsync() {
        FakePageStore storage = NewStore();
        string hash = PageHarness.Stored(storage, "/page").Hash;

        return EditorAsync(storage, "/page", $"cmd=revision&hash={Uri.EscapeDataString(hash)}");
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
                new PageLayoutConfiguration(),
                new RenderOptions(),
                TestContentTypes.Shipped,
                new PageStorageOptions(),
                new PageFetchOptions(),
                new PageReadOptions(),
                new FakePageStateStore(),
                new FakePageReadStore(),
                new FakePageFetcher(),
                new PageMetrics(),
                new TestAntiforgery(),
                new MarkdownRenderer()));

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
            new MarkdownRenderer());
        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> AssistantDeleteAsync() {
        DefaultHttpContext context = AuthenticatedContext("/assistant/1/delete");
        await AssistantEndpoint.DeleteConfirmationAsync(context, 1, new ShellConversationStore(), new TestAntiforgery());
        return await TestHttp.ReadBodyAsync(context);
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
        Dictionary<string, StringValues> noFields = new(StringComparer.Ordinal);
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

    private static async Task<string> EditorAsync(FakePageStore storage, string? path, string command = "") {
        using PageHarness pages = new(storage);

        return await HandleEditorAsync(pages, path, command);
    }

    // A stored compilation carrying a warning, so the editor's diagnostics block is rendered.
    private static async Task<string> EditorWithDiagnosticsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        storage["/page"] = new PageTemplate(PageHarness.Key("/page"), Source, new PageImage(
            TemplateFingerprint.Of(Source),
            pages.Compiler.RuntimeId,
            null,
            null,
            [new PageDiagnostic(PageDiagnosticSeverity.Warning, new TemplatePosition(1, 4), "CS0219: unused variable")]));

        return await HandleEditorAsync(pages, "/page", string.Empty);
    }

    private static async Task<string> HandleEditorAsync(PageHarness pages, string? path, string command) {
        string requestPath = path is null ? "/edit" : "/edit" + PageHarness.Key(path).Key;
        DefaultHttpContext context = TestHttp.CreateContext(path, "GET", requestPath);

        if (command.Length > 0) {
            context.Request.QueryString = new QueryString("?" + command);
        }

        await EditorEndpoint.HandleAsync(context, pages.Editor, Reserved, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    // Two seeded rows, so the listing's restore forms are in the accessibility sweep: one edited seed
    // and one that already matches, which renders its button disabled.
    private static FakePageStore NewStore() => new() {
        ["/page"] = new PageTemplate(PageHarness.Key("/page"), Source),
        ["/notes"] = new PageTemplate(PageHarness.Key("/notes"), Source),
        ["/assistant.css"] = new PageTemplate(PageHarness.Key("/assistant.css"), "/* edited */"),
        ["/editor.css"] = new PageTemplate(PageHarness.Key("/editor.css"), Seed("editor.css")),
    };

    private static string Seed(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds", name));

    private const string BrokenSource = "<% int value = ; %>";

    private static PageImage Broken(string runtimeId) => new(
        TemplateFingerprint.Of(BrokenSource),
        runtimeId,
        null,
        null,
        [new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 16), "CS1525: Invalid expression term ';'")]);
}
