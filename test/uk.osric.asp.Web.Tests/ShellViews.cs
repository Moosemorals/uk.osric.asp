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
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Errors;
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
        "listing",
        "editor",
        "editor-with-diagnostics",
        "history",
        "revision",
        "generated",
        "delete-confirmation",
        "restore",
        "status",
        "error",
        "error-details",
        "compile-error",
    ];

    /// <summary>Renders one of <see cref="Names"/>.</summary>
    /// <param name="name">The view to render.</param>
    public static Task<ShellView> RenderAsync(string name) => name switch {
        "login" => RenderAsync(name, LoginAsync),
        "login-failed" => RenderAsync(name, FailedLoginAsync),
        "listing" => RenderAsync(name, ListingAsync),
        "editor" => RenderAsync(name, () => EditorAsync(NewStore(), path: "/page")),
        "editor-with-diagnostics" => RenderAsync(name, EditorWithDiagnosticsAsync),
        "history" => RenderAsync(name, () => EditorAsync(NewStore(), "/page", "cmd=history")),
        "revision" => RenderAsync(name, RevisionAsync),
        "generated" => RenderAsync(name, () => EditorAsync(NewStore(), "/page", "cmd=generated")),
        "delete-confirmation" => RenderAsync(name, () => EditorAsync(NewStore(), "/page", "cmd=delete")),
        "restore" => RenderAsync(name, RestoreAsync),
        "status" => RenderAsync(name, StatusAsync),
        "error" => RenderAsync(name, () => ErrorAsync(withDetails: false)),
        "error-details" => RenderAsync(name, () => ErrorAsync(withDetails: true)),
        "compile-error" => RenderAsync(name, CompileErrorAsync),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such shell view."),
    };

    private static async Task<ShellView> RenderAsync(string name, Func<Task<string>> render) =>
        new(name, await render());

    private static async Task<string> LoginAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/login");

        await LoginEndpoint.GetAsync(context, new TestAntiforgery());

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
            new RecordingAuthenticationService());

        return await TestHttp.ReadBodyAsync(context);
    }

    // Two live pages and one deleted, so the listing shows a row with every action and a row with the
    // shorter set: the ragged shape the listing keeps as a list.
    private static async Task<string> ListingAsync() {
        FakePageStore storage = NewStore();
        await storage.DeletePageAsync(PageHarness.Key("/notes"));

        return await EditorAsync(storage, path: null);
    }

    private static Task<string> RevisionAsync() {
        FakePageStore storage = NewStore();
        string hash = PageHarness.Stored(storage, "/page").Hash;

        return EditorAsync(storage, "/page", $"cmd=revision&hash={Uri.EscapeDataString(hash)}");
    }

    private static async Task<string> RestoreAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/restore");
        context.Request.QueryString = new QueryString("?path=%2Feditor.css");

        await AdminRestoreEndpoint.GetAsync(context, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> StatusAsync() {
        MetricsDashboard dashboard = new(
            new FakeMetricsSource(_ => new MetricsMatrix([
                new ChartSeries("ok", [
                    new ChartPoint(DateTimeOffset.Parse("2026-11-02T05:30:00Z", CultureInfo.InvariantCulture), 1),
                ]),
            ])),
            new MetricsOptions {
                PrometheusUrl = new Uri("http://prometheus:9090"),
                TimeZone = TimeZoneInfo.Utc,
            },
            new TestClock(),
            NullLogger<MetricsDashboard>.Instance);
        DefaultHttpContext context = TestHttp.CreateContext(null);

        await StatusEndpoint.HandleAsync(context, dashboard, CancellationToken.None);

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
        storage["/broken"] = new StoredPage(PageHarness.Key("/broken"), BrokenSource, Broken(pages.Compiler.RuntimeId));
        DefaultHttpContext context = TestHttp.CreateContext("broken", "GET", "/broken");

        await PageEndpoint.Handle(context, pages.Renderer, NullLoggerFactory.Instance, Reserved, new RenderOptions());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static async Task<string> EditorAsync(FakePageStore storage, string? path, string command = "") {
        using PageHarness pages = new(storage);

        return await HandleEditorAsync(storage, pages, path, command);
    }

    // A stored compilation carrying a warning, so the editor's diagnostics block is rendered.
    private static async Task<string> EditorWithDiagnosticsAsync() {
        FakePageStore storage = new();
        using PageHarness pages = new(storage);
        storage["/page"] = new StoredPage(PageHarness.Key("/page"), Source, new PageCompilation(
            ContentFingerprint.Of(Source),
            pages.Compiler.RuntimeId,
            null,
            null,
            [new PageDiagnostic(PageDiagnosticSeverity.Warning, new TemplatePosition(1, 4), "CS0219: unused variable")]));

        return await HandleEditorAsync(storage, pages, "/page", string.Empty);
    }

    private static async Task<string> HandleEditorAsync(FakePageStore storage, PageHarness pages, string? path, string command) {
        string requestPath = path is null ? "/edit" : "/edit" + PageHarness.Key(path).Key;
        DefaultHttpContext context = TestHttp.CreateContext(path, "GET", requestPath);

        if (command.Length > 0) {
            context.Request.QueryString = new QueryString("?" + command);
        }

        await EditorEndpoint.Handler(context, pages.Editor, Reserved, new TestAntiforgery());

        return await TestHttp.ReadBodyAsync(context);
    }

    private static FakePageStore NewStore() => new() {
        ["/page"] = new StoredPage(PageHarness.Key("/page"), Source),
        ["/notes"] = new StoredPage(PageHarness.Key("/notes"), Source),
    };

    private const string BrokenSource = "<% int value = ; %>";

    private static PageCompilation Broken(string runtimeId) => new(
        ContentFingerprint.Of(BrokenSource),
        runtimeId,
        null,
        null,
        [new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 16), "CS1525: Invalid expression term ';'")]);
}
