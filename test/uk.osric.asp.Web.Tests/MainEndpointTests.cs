// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Diagnostics;
using System.Text;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Endpoints;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class MainEndpointTests {

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);
    private static readonly RenderOptions Options = new();
    private readonly RecordingCompiler _compiler = new(new TemplateCompiler());
    private readonly PageMetrics _metrics = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    [TestCase(null, "/", "/")]
    [TestCase("", "/", "/")]
    [TestCase("   ", "/", "/")]
    [TestCase("missing.cs", "/missing.cs", "/missing.cs")]
    public async Task MissingPageReturnsNotFoundAsync(string? path, string expectedKey, string requestPath) {
        DefaultHttpContext context = TestHttp.CreateContext(path, "GET", requestPath);
        FakeStorage storage = new();

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo($"{expectedKey} not found."));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo(expectedKey));
    }

    [TestCase("/Hello", "", "/hello")]
    [TestCase("/hello/", "", "/hello")]
    [TestCase("/a/../b", "", "/b")]
    [TestCase("/Hello world", "", "/hello%20world")]
    [TestCase("/Caf\u00e9", "", "/caf%C3%A9")]
    [TestCase("/Hello", "?q=1", "/hello?q=1")]
    [TestCase("/search/", "?q=metrics", "/search?q=metrics")]
    [TestCase("/Hello", "?q=a%20b", "/hello?q=a%20b")]
    public async Task NonCanonicalPathRedirectsPermanentlyAsync(string requestPath, string query, string expectedLocation) {
        DefaultHttpContext context = TestHttp.CreateContext(requestPath.TrimStart('/'), "GET", requestPath);
        context.Request.QueryString = new QueryString(query);
        FakeStorage storage = new();

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    // A URL that encodes a delimiter names no page: the route value is decoded to a '?', so the key
    // stops being a path. Answered before the canonical redirect, which could only target a key that
    // cannot exist, and before storage, which would serve a name no request can produce.
    [TestCase("hello?world", "/hello?world")]
    [TestCase("hello#world", "/hello#world")]
    [TestCase("Hello?world", "/Hello?world")]
    public async Task AKeyWithADelimiterIsNotFoundAsync(string path, string requestPath) {
        DefaultHttpContext context = TestHttp.CreateContext(path, "GET", requestPath);
        FakeStorage storage = new();
        storage[PagePath.Normalize(path).Path] = new StoredPage(PagePath.Normalize(path), "should not be served");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.Headers.Location, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo($"{PagePath.Normalize(path)} not found."));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task AQueryStringDoesNotChangeThePageKeyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "GET", "/hello.cs");
        context.Request.QueryString = new QueryString("?from=rss");
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "Hello.");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo("/hello.cs"));
    }

    [Test]
    public async Task StoredHandlerIsCompiledAndExecutedAsync() {
        DefaultHttpContext context = CreateContext("pages/hello.cs");
        FakeStorage storage = new();
        storage["/pages/hello.cs"] = new StoredPage(
            PagePath.Normalize("/pages/hello.cs"),
            "<% throw new System.InvalidOperationException(\"Template executed.\"); %>");

        InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options));

        Assert.That(failure?.Message, Is.EqualTo("Template executed."));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo("/pages/hello.cs"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task SuccessfulTemplateIsRenderedAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "Hello, <% ctx.Write(\"world\"); %>!");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task PageLogsAreFiledUnderThePageCategory() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "<% ctx.Log(\"from the page\"); %>");
        RecordingLoggerFactory logs = new();

        await MainEndpoint.Handle(context, CreatePages(storage), logs, Reserved, Options);

        Assert.That(logs.Entries, Has.Count.EqualTo(1));
        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages./hello.cs"));
        Assert.That(logs.Entries[0].Message, Is.EqualTo("from the page"));
    }

    [Test]
    public async Task StoredCompilationIsRenderedWithoutCompilingAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        TemplateCompiler compiler = new();
        CompileResult compiled = compiler.Compile(Generate("Hello."));
        PageCompilation compilation = new(
            ContentFingerprint.Of("Hello."), TemplateCompiler.RuntimeId, compiled.Image, compiled.Diagnostics);
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "Hello.", compilation);
        int compiles = _compiler.CompileCount;

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task InvalidStoredSourceReturnsCompilationErrorAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakeStorage storage = new();
        storage["/broken.cs"] = new StoredPage(PagePath.Normalize("/broken.cs"), "<% int value = ; %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Handler failed to compile.").And.Contain("error CS"));
    }

    [Test]
    public async Task CompilationDiagnosticsAreHtmlEncodedAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakeStorage storage = new();
        storage["/broken.cs"] = new StoredPage(
            PagePath.Normalize("/broken.cs"), "<% int value = new System.Collections.Generic.List<int>(); %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(body, Does.Not.Contain("List<int>"));
        Assert.That(body, Does.Contain("List&lt;int&gt;"));
    }

    [Test]
    public async Task StorageFailurePropagatesAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakeStorage storage = new() { Failure = new InvalidOperationException("Storage unavailable.") };

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options));

        Assert.That(thrown?.Message, Is.EqualTo("Storage unavailable."));
    }

    [Test]
    public async Task ACancelledRequestNeverReadsStorageAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        FakeStorage storage = new();

        await Assert.CatchAsync<OperationCanceledException>(() =>
            MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options));

        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task StoredPageAtAReservedKeyIsNotFoundAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        FakeStorage storage = new();
        storage["/site.css"] = new StoredPage(PagePath.Normalize("/site.css"), "hijacked");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("/site.css not found."));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task PageStatusAndHeadersAreAppliedAsync() {
        DefaultHttpContext context = CreateContext("status.cs");
        FakeStorage storage = new();
        storage["/status.cs"] = new StoredPage(PagePath.Normalize("/status.cs"),
            "<% ctx.SetStatus(201); ctx.SetHeader(\"X-Page\", \"yes\"); ctx.Write(\"done\"); %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(201));
        Assert.That(context.Response.Headers["X-Page"].ToString(), Is.EqualTo("yes"));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("done"));
    }

    // The encoding matrix belongs to Redirects.To and is covered there; this is the wiring: the page's
    // target reaches the header and no body is written with it.
    [TestCase("/elsewhere")]
    [TestCase("/search?q=metrics")]
    public async Task PageRedirectIsSentWithNoBodyAsync(string location) {
        DefaultHttpContext context = CreateContext("moved.cs");
        FakeStorage storage = new();
        storage["/moved.cs"] = new StoredPage(PagePath.Normalize("/moved.cs"), $"<% ctx.Redirect(\"{location}\"); %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(location));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task PostPageReadsTheFormAsync() {
        byte[] body = Encoding.UTF8.GetBytes("name=Osric&repeat=first&repeat=second");
        DefaultHttpContext context = TestHttp.CreateContext("form.cs", "POST", "/form.cs");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);

        FakeStorage storage = new();
        storage["/form.cs"] = new StoredPage(PagePath.Normalize("/form.cs"),
            "<% ctx.Write($\"{ctx.Request.Form[\"name\"]}/{ctx.Request.Form[\"repeat\"]}\"); %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Osric/first"));
    }

    [Test]
    public async Task HeadRequestWritesNoBodyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "HEAD", "/hello.cs");
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "Hello, world!");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task OversizedRequestIsRejectedBeforeThePageRunsAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("form.cs", "POST", "/form.cs");
        context.Request.ContentLength = 2 * 1024 * 1024;
        FakeStorage storage = new();

        await MainEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(413));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("too large"));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    // The page burns CPU against a wall clock, so this one keeps the process to itself: another
    // fixture's compiles would starve the deadline continuation and answer late.
    [NonParallelizable]
    public async Task APageThatOutrunsTheBudgetAnswersGatewayTimeoutAsync() {
        DefaultHttpContext context = CreateContext("slow.cs");
        FakeStorage storage = new();
        storage["/slow.cs"] = new StoredPage(PagePath.Normalize("/slow.cs"),
            "<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(1500); while (System.DateTime.UtcNow < until) { } %>");
        RenderOptions options = new() { TimeoutSeconds = 1 };

        long start = Stopwatch.GetTimestamp();
        await MainEndpoint.Handle(context, CreatePages(storage, options), NullLoggerFactory.Instance, Reserved, options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(504));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("/slow.cs is taking too long."));
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromSeconds(1.4)));
    }

    [Test]
    public async Task APageThatWritesPastTheBodyCapFailsAsync() {
        DefaultHttpContext context = CreateContext("chatty.cs");
        FakeStorage storage = new();
        storage["/chatty.cs"] = new StoredPage(PagePath.Normalize("/chatty.cs"), "0123456789");
        RenderOptions options = new() { MaxBodyChars = 5 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MainEndpoint.Handle(context, CreatePages(storage, options), NullLoggerFactory.Instance, Reserved, options));
    }

    private static string Generate(string template) {
        TemplateParseResult parsed = TemplateSourceGenerator.GenerateTemplateSource(template);
        Assert.That(parsed.Success, Is.True, string.Join(Environment.NewLine, parsed.Errors));

        // The assertion above proves source was generated.
        return parsed.Source!;
    }

    private PageService CreatePages(IStorage storage, RenderOptions? options = null) {
        RenderOptions render = options ?? Options;

        return new PageService(
            storage,
            _compiler,
            _metrics,
            new PageBudget(render, new CompileOptions()),
            new PageHealth(render.AbandonedLimit),
            NullLogger<PageService>.Instance);
    }

    private static DefaultHttpContext CreateContext(string? path) {
        return TestHttp.CreateContext(path, "GET", PagePath.Normalize(path).Path);
    }
}
