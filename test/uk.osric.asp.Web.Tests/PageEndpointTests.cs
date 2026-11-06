// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Diagnostics;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageEndpointTests {

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);
    private static readonly RenderOptions Options = new();
    private readonly RecordingCompiler _compiler = new(new PageCompiler());
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
        FakePageStore storage = new();

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo($"{expectedKey} not found."));
        Assert.That(storage.LastReadPath?.Key, Is.EqualTo(expectedKey));
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
        FakePageStore storage = new();

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

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
        FakePageStore storage = new();
        storage[PagePath.Normalize(path).Key] = new PageTemplate(PagePath.Normalize(path), "should not be served");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.Headers.Location, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo($"{PagePath.Normalize(path)} not found."));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task AQueryStringDoesNotChangeThePageKeyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "GET", "/hello.cs");
        context.Request.QueryString = new QueryString("?from=rss");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello.");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(storage.LastReadPath?.Key, Is.EqualTo("/hello.cs"));
    }

    [Test]
    public async Task StoredHandlerIsCompiledAndExecutedAsync() {
        DefaultHttpContext context = CreateContext("pages/hello.cs");
        FakePageStore storage = new();
        storage["/pages/hello.cs"] = new PageTemplate(
            PagePath.Normalize("/pages/hello.cs"),
            "<% throw new System.InvalidOperationException(\"Template executed.\"); %>");

        InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options));

        Assert.That(failure?.Message, Is.EqualTo("Template executed."));
        Assert.That(storage.LastReadPath?.Key, Is.EqualTo("/pages/hello.cs"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task SuccessfulTemplateIsRenderedAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, <% ctx.Write(\"world\"); %>!");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task WriteExpressionsEscapeAndRawWriteExpressionsPreserveTheirValuesAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(
            PagePath.Normalize("/hello.cs"),
            "<p><%= \"<tag>&\" %>|<%== \"<tag>&\" %></p>");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("<p>&lt;tag&gt;&amp;|<tag>&</p>"));
    }

    [Test]
    public async Task PageLogsAreFiledUnderThePageCategory() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "<% ctx.Log(\"from the page\"); %>");
        RecordingLoggerFactory logs = new();

        await PageEndpoint.Handle(context, CreatePages(storage), logs, Reserved, Options);

        Assert.That(logs.Entries, Has.Count.EqualTo(1));
        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages./hello.cs"));
        Assert.That(logs.Entries[0].Message, Is.EqualTo("from the page"));
    }

    [Test]
    public async Task StoredCompilationIsRenderedWithoutCompilingAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        PageCompiler compiler = new();
        CompileResult compiled = compiler.Compile(Generate("Hello."));
        PageImage compilation = new(
            TemplateFingerprint.Of("Hello."), PageCompiler.RuntimeId, compiled.Image, compiled.Symbols, compiled.Diagnostics);
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello.", compilation);
        int compiles = _compiler.CompileCount;

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task InvalidStoredSourceReturnsCompilationErrorAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakePageStore storage = new();
        storage["/broken.cs"] = new PageTemplate(PagePath.Normalize("/broken.cs"), "<% int value = ; %>");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Handler failed to compile.").And.Contain("error CS"));
    }

    [Test]
    public async Task CompilationDiagnosticsAreHtmlEncodedAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakePageStore storage = new();
        storage["/broken.cs"] = new PageTemplate(
            PagePath.Normalize("/broken.cs"), "<% int value = new System.Collections.Generic.List<int>(); %>");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(body, Does.Not.Contain("List<int>"));
        Assert.That(body, Does.Contain("List&lt;int&gt;"));
    }

    [Test]
    public async Task StorageFailurePropagatesAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakePageStore storage = new() { Failure = new InvalidOperationException("Storage unavailable.") };

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options));

        Assert.That(thrown?.Message, Is.EqualTo("Storage unavailable."));
    }

    [Test]
    public async Task ACancelledRequestNeverReadsStorageAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        FakePageStore storage = new();

        await Assert.CatchAsync<OperationCanceledException>(() =>
            PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options));

        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task StoredPageAtAReservedKeyIsNotFoundAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("favicon.ico", "GET", "/favicon.ico");
        FakePageStore storage = new();
        storage["/favicon.ico"] = new PageTemplate(PagePath.Normalize("/favicon.ico"), "hijacked");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("/favicon.ico not found."));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task PageStatusAndHeadersAreAppliedAsync() {
        DefaultHttpContext context = CreateContext("status.cs");
        FakePageStore storage = new();
        storage["/status.cs"] = new PageTemplate(PagePath.Normalize("/status.cs"),
            "<% ctx.SetStatus(201); ctx.SetHeader(\"X-Page\", \"yes\"); ctx.Write(\"done\"); %>");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(201));
        Assert.That(context.Response.Headers["X-Page"].ToString(), Is.EqualTo("yes"));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("done"));
    }

    // The encoding matrix belongs to Redirect.To and is covered there; this is the wiring: the page's
    // target reaches the header and no body is written with it.
    [TestCase("/elsewhere")]
    [TestCase("/search?q=metrics")]
    public async Task PageRedirectIsSentWithNoBodyAsync(string location) {
        DefaultHttpContext context = CreateContext("moved.cs");
        FakePageStore storage = new();
        storage["/moved.cs"] = new PageTemplate(PagePath.Normalize("/moved.cs"), $"<% ctx.Redirect(\"{location}\"); %>");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

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

        FakePageStore storage = new();
        storage["/form.cs"] = new PageTemplate(PagePath.Normalize("/form.cs"),
            "<% ctx.Write($\"{ctx.Request.Form[\"name\"]}/{ctx.Request.Form[\"repeat\"]}\"); %>");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Osric/first"));
    }

    [Test]
    public async Task HeadRequestWritesNoBodyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "HEAD", "/hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount("Hello, world!")));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [TestCase("GET")]
    [TestCase("HEAD")]
    public async Task PageResponseSetsContentLengthFromUtf8BytesAsync(string method) {
        const string body = "caf\u00e9 \U0001F680";
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", method, "/hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), body);

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(body)));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo(method == "HEAD" ? string.Empty : body));
    }

    [TestCase("GET")]
    [TestCase("HEAD")]
    public async Task MatchingIfNoneMatchReturnsBodylessNotModifiedAsync(string method) {
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");
        DefaultHttpContext initial = TestHttp.CreateContext("hello.cs", method, "/hello.cs");

        await PageEndpoint.Handle(initial, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        DefaultHttpContext conditional = TestHttp.CreateContext("hello.cs", method, "/hello.cs");
        conditional.Request.Headers.IfNoneMatch = initial.Response.Headers.ETag;

        await PageEndpoint.Handle(conditional, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(conditional.Response.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
        Assert.That(conditional.Response.Headers.ETag, Is.EqualTo(initial.Response.Headers.ETag));
        Assert.That(await TestHttp.ReadBodyAsync(conditional), Is.Empty);
    }

    [Test]
    public async Task PostIgnoresIfNoneMatchAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "POST", "/hello.cs");
        context.Request.Headers.IfNoneMatch = "*";
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task IfNoneMatchWildcardMatchesAnExistingPageAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "GET", "/hello.cs");
        context.Request.Headers.IfNoneMatch = "*";
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task OversizedRequestIsRejectedBeforeThePageRunsAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("form.cs", "POST", "/form.cs");
        context.Request.ContentLength = 2 * 1024 * 1024;
        FakePageStore storage = new();

        await PageEndpoint.Handle(context, CreatePages(storage), NullLoggerFactory.Instance, Reserved, Options);

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
        FakePageStore storage = new();
        storage["/slow.cs"] = new PageTemplate(PagePath.Normalize("/slow.cs"),
            "<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(1500); while (System.DateTime.UtcNow < until) { } %>");
        RenderOptions options = new() { TimeoutSeconds = 1 };

        long start = Stopwatch.GetTimestamp();
        await PageEndpoint.Handle(context, CreatePages(storage, options), NullLoggerFactory.Instance, Reserved, options);

        Assert.That(context.Response.StatusCode, Is.EqualTo(504));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("/slow.cs is taking too long."));
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromSeconds(1.4)));
    }

    [Test]
    public async Task APageThatWritesPastTheBodyCapFailsAsync() {
        DefaultHttpContext context = CreateContext("chatty.cs");
        FakePageStore storage = new();
        storage["/chatty.cs"] = new PageTemplate(PagePath.Normalize("/chatty.cs"), "0123456789");
        RenderOptions options = new() { MaxBodyChars = 5 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PageEndpoint.Handle(context, CreatePages(storage, options), NullLoggerFactory.Instance, Reserved, options));
    }

    private static GeneratedSource Generate(string template) {
        TemplateGenerationResult parsed = TemplateSourceGenerator.Generate(template);
        Assert.That(parsed.Success, Is.True, string.Join(Environment.NewLine, parsed.Errors));

        // The assertion above proves source was generated.
        return parsed.Source!;
    }

    private PageRenderer CreatePages(IPageStore storage, RenderOptions? options = null) {
        RenderOptions render = options ?? Options;
        PageBudget budget = new(render, new CompileOptions());
        PageImageBuilder compilations = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageRenderer(
            storage,
            _compiler,
            compilations,
            _metrics,
            budget,
            new PageHealth(render.AbandonedLimit),
            NullLogger<PageRenderer>.Instance);
    }

    private static DefaultHttpContext CreateContext(string? path) {
        return TestHttp.CreateContext(path, "GET", PagePath.Normalize(path).Key);
    }
}
