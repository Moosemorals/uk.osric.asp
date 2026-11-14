// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageEndpointTests {

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);
    private static readonly RenderOptions Options = TestOptions.Render;
    private static readonly PageStorageOptions StorageOptions = TestOptions.Storage;
    private static readonly PageFetchOptions FetchOptions = TestOptions.Fetch;
    private static readonly PageLayoutConfiguration NoLayout = TestOptions.Layout with { Default = null };
    private readonly RecordingCompiler _compiler = new(new PageCompiler());
    private readonly PageMetrics _metrics = new();
    private readonly TestAntiforgery _antiforgery = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    private PageEnvironment EndpointEnvironment(
        RenderOptions? render = null,
        PageContentTypes? contentTypes = null,
        PageLayoutConfiguration? layout = null) {
        return new PageEnvironment(
            Reserved,
            TestOptions.Editor,
            layout ?? NoLayout,
            render ?? Options,
            contentTypes ?? TestContentTypes.Shipped,
            StorageOptions,
            FetchOptions,
            TestOptions.Read,
            new FakePageStateStore(),
            new FakePageReadStore(),
            Saver(render ?? Options),
            new FakePageFetcher(),
            _metrics,
            _antiforgery,
            new MarkdownRenderer(),
            new DiffRenderer(),
            TestOptions.Session,
            new FakePageSessionStore(),
            TestSessions.Cookie);
    }

    [TestCase(null, "/", "/")]
    [TestCase("", "/", "/")]
    [TestCase("   ", "/", "/")]
    [TestCase("missing.cs", "/missing.cs", "/missing.cs")]
    public async Task MissingPageReturnsNotFoundAsync(string? path, string expectedKey, string requestPath) {
        DefaultHttpContext context = TestHttp.CreateContext(path, "GET", requestPath);
        FakePageStore storage = new();

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain($"{expectedKey} not found."));
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

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

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

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.Headers.Location, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain($"{PagePath.Normalize(path)} not found."));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task AQueryStringDoesNotChangeThePageKeyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "GET", "/hello.cs");
        context.Request.QueryString = new QueryString("?from=rss");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

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
            PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment()));

        Assert.That(failure?.Message, Is.EqualTo("Template executed."));
        Assert.That(storage.LastReadPath?.Key, Is.EqualTo("/pages/hello.cs"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task SuccessfulTemplateIsRenderedAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, <% ctx.Write(\"world\"); %>!");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task AStoredScriptPageNeedsNoDirectiveToBeJavaScriptAsync() {
        DefaultHttpContext context = CreateContext("x.js");
        FakePageStore storage = new();
        storage["/x.js"] = new PageTemplate(PagePath.Normalize("/x.js"), "export const answer = 42;");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/javascript; charset=utf-8"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("export const answer = 42;"));
    }

    [Test]
    public async Task AStoredStylesheetPageCanOverrideItsTypeBackToHtmlAsync() {
        DefaultHttpContext context = CreateContext("x.css");
        FakePageStore storage = new();
        storage["/x.css"] = new PageTemplate(PagePath.Normalize("/x.css"),
            "<% ctx.SetContentType(\"text/html; charset=utf-8\"); %>Styled.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
    }

    [TestCase("GET")]
    [TestCase("HEAD")]
    public async Task AStoredScriptPageIsNeverCachedAsync(string method) {
        FakePageStore storage = new();
        storage["/x.js"] = new PageTemplate(PagePath.Normalize("/x.js"), "export {};");
        DefaultHttpContext context = TestHttp.CreateContext("x.js", method, "/x.js");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.ContentType, Is.EqualTo("text/javascript; charset=utf-8"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(context.Response.Headers.ETag, Is.Empty);
    }

    [Test]
    public async Task AnUnmappedPageTakesTheConfiguredFallbackAsync() {
        DefaultHttpContext context = CreateContext("notes");
        FakePageStore storage = new();
        storage["/notes"] = new PageTemplate(PagePath.Normalize("/notes"), "Notes.");
        PageContentTypes plain = PageContentTypes.Create("text/plain; charset=utf-8", []);

        await PageEndpoint.HandleAsync(
            context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment(contentTypes: plain));

        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
    }

    [Test]
    public async Task APageThatAllowsScriptsWidensItsPolicyAsync() {
        DefaultHttpContext context = CreateContext("scripted.cs");
        FakePageStore storage = new();
        storage["/scripted.cs"] = new PageTemplate(
            PagePath.Normalize("/scripted.cs"), "<% ctx.AllowScripts(); %>Scripted.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.Headers[SecurityHeaders.ContentSecurityPolicy].ToString(),
            Is.EqualTo(SecurityHeaders.WithScripts));
    }

    [Test]
    public async Task APageThatDoesNotAllowScriptsLeavesThePolicyAloneAsync() {
        DefaultHttpContext context = CreateContext("plain.cs");
        FakePageStore storage = new();
        storage["/plain.cs"] = new PageTemplate(PagePath.Normalize("/plain.cs"), "Plain.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.Headers.ContainsKey(SecurityHeaders.ContentSecurityPolicy), Is.False);
    }

    [Test]
    public async Task APageThatRedirectsDoesNotWidenItsPolicyAsync() {
        DefaultHttpContext context = CreateContext("moved.cs");
        FakePageStore storage = new();
        storage["/moved.cs"] = new PageTemplate(
            PagePath.Normalize("/moved.cs"), "<% ctx.AllowScripts(); ctx.Redirect(\"/elsewhere\"); %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.ContainsKey(SecurityHeaders.ContentSecurityPolicy), Is.False);
    }

    [Test]
    public async Task WriteExpressionsEscapeAndRawWriteExpressionsPreserveTheirValuesAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(
            PagePath.Normalize("/hello.cs"),
            "<p><%= \"<tag>&\" %>|<%== \"<tag>&\" %></p>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("<p>&lt;tag&gt;&amp;|<tag>&</p>"));
    }

    [Test]
    public async Task PageLogsAreFiledUnderTheSharedPageCategoryAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "<% ctx.Log(\"from the page\"); %>");
        RecordingLoggerFactory logs = new();

        await PageEndpoint.HandleAsync(context, CreatePages(storage), logs, EndpointEnvironment());

        Assert.That(logs.Entries, Has.Count.EqualTo(1));
        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages"));
        Assert.That(logs.Entries[0].Scope["Path"], Is.EqualTo("/hello.cs"));
        Assert.That(logs.Entries[0].Message, Is.EqualTo("from the page"));
    }

    [Test]
    public async Task UnknownPathsAllShareOneLoggerCategoryAsync() {
        FakePageStore storage = new();
        RecordingLoggerFactory logs = new();

        foreach (string path in new[] { "one", "two", "three", "four.cs" }) {
            DefaultHttpContext context = TestHttp.CreateContext(path, "GET", "/" + path);

            await PageEndpoint.HandleAsync(context, CreatePages(storage), logs, EndpointEnvironment());
        }

        Assert.That(logs.Categories, Is.All.EqualTo("uk.osric.asp.Pages"));
    }

    [Test]
    public async Task StoredCompilationIsRenderedWithoutCompilingAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        PageCompiler compiler = new();
        CompileResult compiled = compiler.Compile(TestTemplates.Generate("Hello."), []);
        PageImage compilation = new(
            TemplateFingerprint.Of("Hello."), PageCompiler.RuntimeId, compiled.Image, compiled.Symbols, compiled.Diagnostics);
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello.", compilation);
        int compiles = _compiler.CompileCount;

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task InvalidStoredSourceReturnsCompilationErrorAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakePageStore storage = new();
        storage["/broken.cs"] = new PageTemplate(PagePath.Normalize("/broken.cs"), "<% int value = ; %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Handler failed to compile.").And.Contain("error CS"));
    }

    [Test]
    public async Task CompilationDiagnosticsAreHtmlEncodedAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakePageStore storage = new();
        storage["/broken.cs"] = new PageTemplate(
            PagePath.Normalize("/broken.cs"), "<% int value = new System.Collections.Generic.List<int>(); %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

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
            PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment()));

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
            PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment()));

        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task StoredPageAtAReservedKeyIsNotFoundAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("favicon.ico", "GET", "/favicon.ico");
        FakePageStore storage = new();
        storage["/favicon.ico"] = new PageTemplate(PagePath.Normalize("/favicon.ico"), "hijacked");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("/favicon.ico not found."));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task PageStatusAndHeadersAreAppliedAsync() {
        DefaultHttpContext context = CreateContext("status.cs");
        FakePageStore storage = new();
        storage["/status.cs"] = new PageTemplate(PagePath.Normalize("/status.cs"),
            "<% ctx.SetStatus(201); ctx.SetHeader(\"X-Page\", \"yes\"); ctx.Write(\"done\"); %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

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

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(location));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
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

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Osric/first"));
    }

    [Test]
    public async Task HeadRequestWritesNoBodyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "HEAD", "/hello.cs");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

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

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(body)));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo(method == "HEAD" ? string.Empty : body));
    }

    [TestCase("GET")]
    [TestCase("HEAD")]
    public async Task IfNoneMatchIsIgnoredAsync(string method) {
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");
        DefaultHttpContext initial = TestHttp.CreateContext("hello.cs", method, "/hello.cs");

        await PageEndpoint.HandleAsync(initial, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        DefaultHttpContext conditional = TestHttp.CreateContext("hello.cs", method, "/hello.cs");
        conditional.Request.Headers.IfNoneMatch = "*";

        await PageEndpoint.HandleAsync(conditional, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(initial.Response.Headers.ETag, Is.Empty, "a stored page carries no validator");
        Assert.That(conditional.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(conditional.Response.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount("Hello, world!")));
        Assert.That(await TestHttp.ReadBodyAsync(conditional), Is.EqualTo(method == "HEAD" ? string.Empty : "Hello, world!"));
    }

    [Test]
    public async Task PostIgnoresIfNoneMatchAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "POST", "/hello.cs");
        context.Request.Headers.IfNoneMatch = "*";
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task AnAnonymousPageIsUncacheableTooAsync() {
        DefaultHttpContext context = CreateContext("public.cs");
        FakePageStore storage = new();
        storage["/public.cs"] = new PageTemplate(PagePath.Normalize("/public.cs"), "Hello.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(context.Response.Headers.ETag, Is.Empty);
    }

    [Test]
    public async Task OversizedRequestIsRejectedBeforeThePageRunsAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("form.cs", "POST", "/form.cs");
        context.Request.ContentLength = 2 * 1024 * 1024;
        FakePageStore storage = new();

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

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
            "<% System.DateTime until = System.DateTime.UtcNow.AddMilliseconds(600); while (System.DateTime.UtcNow < until) { } %>");
        RenderOptions options = TestOptions.Render with { TimeoutSeconds = 0.25 };

        long start = Stopwatch.GetTimestamp();
        await PageEndpoint.HandleAsync(context, CreatePages(storage, options), NullLoggerFactory.Instance, EndpointEnvironment(options));

        Assert.That(context.Response.StatusCode, Is.EqualTo(504));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("/slow.cs is taking too long."));
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromMilliseconds(650)));
    }

    [Test]
    public async Task APageThatWritesPastTheBodyCapFailsAsync() {
        DefaultHttpContext context = CreateContext("chatty.cs");
        FakePageStore storage = new();
        storage["/chatty.cs"] = new PageTemplate(PagePath.Normalize("/chatty.cs"), "0123456789");
        RenderOptions options = TestOptions.Render with { MaxBodyChars = 5 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PageEndpoint.HandleAsync(context, CreatePages(storage, options), NullLoggerFactory.Instance, EndpointEnvironment(options)));
    }

    [Test]
    public async Task AChunkedFormPastTheCapIsRejectedAsync() {
        DefaultHttpContext context = ChunkedForm("field=" + new string('a', 2 * 1024 * 1024));
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(413));
    }

    [Test]
    public async Task AFormTheParserRefusesIsABadRequestAsync() {
        // A field name past the parser's own 2048 character limit, which it signals by throwing.
        DefaultHttpContext context = ChunkedForm(new string('k', 4096) + "=value");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task AChunkedFormUnderTheCapIsServedAsync() {
        DefaultHttpContext context = ChunkedForm("field=hello");
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(
            PagePath.Normalize("/hello.cs"), "<% ctx.Write(ctx.Request.Form[\"field\"]); %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("hello"));
    }

    [Test]
    public async Task AnAnonymousVisitorToAGuardedPageIsSentToLoginAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("private.cs", "GET", "/private.cs");
        context.Request.QueryString = new QueryString("?tab=one&tab=two");
        // A cookie the page can read is not an identity: only the authenticated principal is.
        context.Request.Headers.Cookie = "a=forged";
        FakePageStore storage = new();
        storage["/private.cs"] = new PageTemplate(PagePath.Normalize("/private.cs"), GuardedPage);

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status302Found));
        Assert.That(ReturnUrlOf(context), Is.EqualTo("/private.cs?tab=one&tab=two"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty, "output written before the guard must not be sent");
    }

    [Test]
    public async Task AnAnonymousPostToAGuardedPageSeesOtherAsync() {
        DefaultHttpContext context = PostForm("private.cs", "note=hi");
        FakePageStore storage = new();
        storage["/private.cs"] = new PageTemplate(PagePath.Normalize("/private.cs"), GuardedPage);

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        // A form post that turns into a sign-in must not be replayed as a post on the way back.
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status303SeeOther));
        Assert.That(ReturnUrlOf(context), Is.EqualTo("/private.cs"));
    }

    [Test]
    public async Task ASignedInVisitorSeesThePageAndTheNameAsync() {
        DefaultHttpContext context = CreateContext("private.cs");
        SignIn(context, "owner");
        FakePageStore storage = new();
        storage["/private.cs"] = new PageTemplate(PagePath.Normalize("/private.cs"), GuardedPage);

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("<p>before</p><p>owner</p>"));
    }

    [Test]
    public async Task AnAuthenticatedResponseIsNotCacheableAsync() {
        DefaultHttpContext context = CreateContext("private.cs");
        SignIn(context);
        FakePageStore storage = new();
        storage["/private.cs"] = new PageTemplate(PagePath.Normalize("/private.cs"), "Hello.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(context.Response.Headers.ETag, Is.Empty);
    }


    [Test]
    public async Task AnAuthenticatedPostWithoutAValidTokenIsRefusedBeforeThePageRunsAsync() {
        _antiforgery.RequestIsValid = false;
        DefaultHttpContext context = PostForm("private.cs", "note=hi");
        SignIn(context);
        FakePageStore storage = new();
        storage["/private.cs"] = new PageTemplate(
            PagePath.Normalize("/private.cs"), "<% throw new System.InvalidOperationException(\"the page ran\"); %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("could not be verified"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task AnAuthenticatedPostWithAValidTokenRendersAsync() {
        DefaultHttpContext context = PostForm("private.cs", "note=hi");
        SignIn(context);
        FakePageStore storage = new();
        storage["/private.cs"] = new PageTemplate(
            PagePath.Normalize("/private.cs"), "<% ctx.Write(ctx.Request.Form[\"note\"]); %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("hi"));
    }

    [Test]
    public async Task AnAnonymousPostIsNotSubjectToTheTokenCheckAsync() {
        _antiforgery.RequestIsValid = false;
        DefaultHttpContext context = PostForm("public.cs", "note=hi");
        FakePageStore storage = new();
        storage["/public.cs"] = new PageTemplate(
            PagePath.Normalize("/public.cs"), "<% ctx.Write(ctx.Request.Form[\"note\"]); %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("hi"));
    }

    [Test]
    public async Task TheAntiforgeryFieldIsRenderedOncePerRenderAsync() {
        DefaultHttpContext context = CreateContext("private.cs");
        SignIn(context);
        FakePageStore storage = new();
        storage["/private.cs"] = new PageTemplate(
            PagePath.Normalize("/private.cs"), "<%== ctx.AntiforgeryField %><%== ctx.AntiforgeryField %>");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
        Assert.That(body, Does.Contain("value=\"request-token\""));
        Assert.That(_antiforgery.TokenRequests, Is.EqualTo(1), "asking twice must not mint a second pair");
    }

    private const string GuardedPage =
        "<p>before</p><% if (ctx.User is not { } user) { ctx.RedirectToLogin(); return; } %><p><%= user.Name %></p>";

    private static void SignIn(DefaultHttpContext context, string name = "owner") {
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, name)], "test"));
    }

    private static string ReturnUrlOf(DefaultHttpContext context) {
        string location = context.Response.Headers.Location.ToString();
        Assert.That(location, Does.StartWith("/login?returnUrl="));

        return QueryHelpers.ParseQuery(new Uri("https://site.example" + location).Query)["returnUrl"].ToString();
    }

    // No ContentLength, which is what a chunked request looks like to the endpoint.
    private static DefaultHttpContext PostForm(string path, string body) {
        DefaultHttpContext context = TestHttp.CreateContext(path, "POST", "/" + path);
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context;
    }

    // A save is never made through this fixture's environment; the saver exists because a render is
    // offered the capability the same way every other one is.
    [Test]
    public async Task AVersionedAssetIsServedFromSourceWithAnImmutableCacheAsync() {
        const string Source = "body { color: red; }";
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        FakePageStore storage = StoreAsset("/site.css", Source);
        string hash = PageHarness.Stored(storage, "/site.css").Hash;
        context.Request.QueryString = new QueryString($"?v={hash}");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/css; charset=utf-8"));
        Assert.That(context.Response.Headers.CacheControl.ToString(),
            Is.EqualTo("public, max-age=31536000, immutable"));
        Assert.That(context.Response.Headers.ETag.ToString(), Is.EqualTo($"\"{hash}\""));
        Assert.That(context.Response.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(Source)));
        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo(Source));
        Assert.That(_compiler.CompileCount, Is.Zero);
    }

    [Test]
    public async Task AMatchingValidatorAnswersNotModifiedAsync() {
        const string Source = "body { color: red; }";
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        FakePageStore storage = StoreAsset("/site.css", Source);
        string hash = PageHarness.Stored(storage, "/site.css").Hash;
        context.Request.QueryString = new QueryString($"?v={hash}");
        context.Request.Headers.IfNoneMatch = $"\"{hash}\"";

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
        Assert.That(context.Response.Headers.CacheControl.ToString(),
            Is.EqualTo("public, max-age=31536000, immutable"));
        Assert.That(context.Response.Headers.ETag.ToString(), Is.EqualTo($"\"{hash}\""));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task AnUnversionedAssetIsStillNoStoreWithoutAValidatorAsync() {
        const string Source = "body { color: red; }";
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        FakePageStore storage = StoreAsset("/site.css", Source);

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(context.Response.Headers.ETag, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo(Source));
        Assert.That(_compiler.CompileCount, Is.Zero, "a no-code asset is never compiled");
    }

    [Test]
    public async Task AVersionedAssetAnswersHeadWithItsLengthAndNoBodyAsync() {
        const string Source = "body { color: red; }";
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "HEAD", "/site.css");
        FakePageStore storage = StoreAsset("/site.css", Source);
        string hash = PageHarness.Stored(storage, "/site.css").Hash;
        context.Request.QueryString = new QueryString($"?v={hash}");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.ContentLength, Is.EqualTo(Encoding.UTF8.GetByteCount(Source)));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [TestCase("POST")]
    [TestCase("PUT")]
    public async Task AVersionedAssetRefusesAMethodThatCouldWriteAsync(string method) {
        DefaultHttpContext context = TestHttp.CreateContext("site.css", method, "/site.css");
        FakePageStore storage = StoreAsset("/site.css", "body { }");
        string hash = PageHarness.Stored(storage, "/site.css").Hash;
        context.Request.QueryString = new QueryString($"?v={hash}");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status405MethodNotAllowed));
        Assert.That(context.Response.Headers.Allow.ToString(), Is.EqualTo("GET, HEAD"));
    }

    [TestCase("?v=not-a-token")]
    [TestCase("?v=")]
    [TestCase("?v=a&v=b")]
    public async Task AMalformedOrRepeatedTokenIsNotFoundAsync(string query) {
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        FakePageStore storage = StoreAsset("/site.css", "body { }");
        context.Request.QueryString = new QueryString(query);

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task ATokenStoredUnderAnotherPathIsNotFoundAsync() {
        FakePageStore storage = StoreAsset("/site.css", "body { }");
        StoreAsset(storage, "/other.css", "p { }");
        string hash = PageHarness.Stored(storage, "/other.css").Hash;
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        context.Request.QueryString = new QueryString($"?v={hash}");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task ALibraryKeyIsNotFoundAsync() {
        FakePageStore storage = StoreLibrary("/_lib/document");
        DefaultHttpContext context = TestHttp.CreateContext("_lib/document", "GET", "/_lib/document");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound),
            "a library has no representation to serve");
    }

    [Test]
    public async Task AVersionedLibraryRevisionIsNotFoundAsync() {
        FakePageStore storage = StoreLibrary("/_lib/document");
        string hash = PageHarness.Stored(storage, "/_lib/document").Hash;
        DefaultHttpContext context = TestHttp.CreateContext("_lib/document", "GET", "/_lib/document");
        context.Request.QueryString = new QueryString($"?v={hash}");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task AnOldRevisionStillServesItsBytesAfterANewerSaveAsync() {
        FakePageStore storage = StoreAsset("/site.css", "body { color: red; }");
        string oldHash = PageHarness.Stored(storage, "/site.css").Hash;
        storage["/site.css"] = PageTemplate.NewRevision(
            PageHarness.Key("/site.css"),
            "body { color: blue; }",
            new PageImage(TemplateFingerprint.Of("body { color: blue; }"), _compiler.RuntimeId, null, null, [], Kind: PageKind.Text),
            _compiler.RuntimeId);
        string newHash = PageHarness.Stored(storage, "/site.css").Hash;

        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        context.Request.QueryString = new QueryString($"?v={oldHash}");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("body { color: red; }"));
        Assert.That(newHash, Is.Not.EqualTo(oldHash),
            "an edit moves the hash a fresh link would carry");
    }

    [Test]
    public async Task AVersionedRevisionWithCodeRendersButIsNeverCachedAsync() {
        FakePageStore storage = new();
        PageTemplate old = PageTemplate.NewRevision(
            PageHarness.Key("/page"),
            "<% ctx.Write(\"old\"); %>",
            null,
            _compiler.RuntimeId);
        storage["/page"] = old;
        storage["/page"] = PageTemplate.NewRevision(
            PageHarness.Key("/page"),
            "<% ctx.Write(\"new\"); %>",
            null,
            _compiler.RuntimeId);
        DefaultHttpContext context = TestHttp.CreateContext("page", "GET", "/page");
        context.Request.QueryString = new QueryString($"?v={old.Hash}");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("old"));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(context.Response.Headers.ETag, Is.Empty);
    }

    [Test]
    public async Task ATombstonedAssetAnswersAtItsVersionedUrlButNotItsOwnAsync() {
        FakePageStore storage = StoreAsset("/site.css", "body { }");
        string hash = PageHarness.Stored(storage, "/site.css").Hash;
        await storage.DeletePageAsync(PageHarness.Key("/site.css"));

        DefaultHttpContext versioned = TestHttp.CreateContext("site.css", "GET", "/site.css");
        versioned.Request.QueryString = new QueryString($"?v={hash}");
        await PageEndpoint.HandleAsync(versioned, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        DefaultHttpContext bare = TestHttp.CreateContext("site.css", "GET", "/site.css");
        await PageEndpoint.HandleAsync(bare, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(versioned.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(bare.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task AScrubbedAssetNoLongerAnswersAtItsVersionedUrlAsync() {
        FakePageStore storage = StoreAsset("/site.css", "body { }");
        string hash = PageHarness.Stored(storage, "/site.css").Hash;
        await storage.DeletePageAsync(PageHarness.Key("/site.css"));
        await storage.ScrubPageAsync(PageHarness.Key("/site.css"));

        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        context.Request.QueryString = new QueryString($"?v={hash}");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task ANoCodePageIsServedRawWhateverTheLayoutPolicyIsAsync() {
        const string Source = "<!DOCTYPE html><html><head><title>Mine</title></head><body><p>Own document</p></body></html>";
        DefaultHttpContext context = TestHttp.CreateContext("mine.html", "GET", "/mine.html");
        FakePageStore storage = StoreAsset("/mine.html", Source);

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance,
            EndpointEnvironment(layout: TestOptions.Layout with { Default = "/_layout" }));

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(body, Is.EqualTo(Source));
        Assert.That(body, Does.Not.Contain("site-header"));
        Assert.That(body, Does.Not.Contain("/site.css"));
    }

    [Test]
    public async Task TheScriptPolicyFollowsTheExtensionAsync() {
        const string Source = "<p>Own body</p>";
        PageContentTypes withSelf = PageContentTypes.Create(
            new PageContentTypeRule("text/html; charset=utf-8"),
            [new KeyValuePair<string, PageContentTypeRule>(
                ".htmlx", new PageContentTypeRule("text/html; charset=utf-8", PageScriptPolicy.Self))]);
        DefaultHttpContext context = TestHttp.CreateContext("mine.htmlx", "GET", "/mine.htmlx");
        FakePageStore storage = StoreAsset("/mine.htmlx", Source);

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance,
            EndpointEnvironment(contentTypes: withSelf, layout: TestOptions.Layout with { Default = "/_layout" }));

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.Headers["Content-Security-Policy"].ToString(), Does.Contain("script-src 'self'"));
    }

    [Test]
    public async Task ADocumentWithCodeCanStillChooseTheLayoutAsync() {
        FakePageStore storage = new();
        storage["/_layout"] = new PageTemplate(PageHarness.Key("/_layout"), """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><html><head><title><%= layout.Title %></title></head><body><%== layout.Body %></body></html>
            """);
        storage["/mine.html"] = new PageTemplate(PageHarness.Key("/mine.html"), """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); layout.Layout = "/_layout"; layout.Title = "Mine"; %><p>Own body</p>
            """);
        DefaultHttpContext context = TestHttp.CreateContext("mine.html", "GET", "/mine.html");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance,
            EndpointEnvironment(layout: TestOptions.Layout with { Default = "/_layout" }));

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("<title>Mine</title>"));
        Assert.That(body, Does.Contain("<p>Own body</p>"));
    }

    [Test]
    public async Task ANoCodeHtmlFragmentIsItsOwnRepresentationAsync() {
        FakePageStore storage = StoreAsset("/fragment.html", "<p>Fragment</p>");

        DefaultHttpContext context = TestHttp.CreateContext("fragment.html", "GET", "/fragment.html");
        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance,
            EndpointEnvironment(layout: TestOptions.Layout with { Default = "/_layout" }));

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("<p>Fragment</p>"));
    }

    [Test]
    public async Task AnHtmKeyIsStillAFragmentAndIsWrappedAsync() {
        FakePageStore storage = new();
        storage["/_layout"] = new PageTemplate(PageHarness.Key("/_layout"), """
            <% var layout = ctx.Capabilities.GetRequired<IPageLayout>(); %><html><head><title><%= layout.Title %></title></head><body><%== layout.Body %></body></html>
            """);
        storage["/doc.htm"] = new PageTemplate(PageHarness.Key("/doc.htm"), """
            <% ctx.Capabilities.GetRequired<IPageLayout>().Title = "Doc"; %><p>Fragment</p>
            """);
        DefaultHttpContext context = TestHttp.CreateContext("doc.htm", "GET", "/doc.htm");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance,
            EndpointEnvironment(layout: TestOptions.Layout with { Default = "/_layout" }));

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("<title>Doc</title>"));
        Assert.That(body, Does.Contain("<p>Fragment</p>"));
    }

    private static FakePageStore StoreAsset(string path, string source) {
        FakePageStore storage = new();
        StoreAsset(storage, path, source);

        return storage;
    }

    private static void StoreAsset(FakePageStore storage, string path, string source) {
        PagePath key = PageHarness.Key(path);
        storage[path] = PageTemplate.NewRevision(
            key,
            source,
            new PageImage(TemplateFingerprint.Of(source), "test:runtime", null, null, [], Kind: PageKind.Text),
            "test:runtime");
    }

    private static FakePageStore StoreLibrary(string path) {
        const string Source = "<%! namespace Site { class Document { } } %>";
        FakePageStore storage = new();
        storage[path] = PageTemplate.NewRevision(
            PageHarness.Key(path),
            Source,
            new PageImage(TemplateFingerprint.Of(Source), "test:runtime", null, null, [], PageKind.Library),
            "test:runtime");

        return storage;
    }

    private PageSaver Saver(RenderOptions render) {
        PageBudget budget = new(render, TestOptions.Compile);
        PageImageBuilder images = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageSaver(
            new FakePageRevisionStore(new FakePageStore()), images, new PageLibraries(new FakePageReadStore()),
            _metrics, NullLogger<PageSaver>.Instance);
    }

    private PageRenderer CreatePages(IPageStore storage, RenderOptions? options = null) {
        RenderOptions render = options ?? Options;
        PageBudget budget = new(render, TestOptions.Compile);
        PageImageBuilder compilations = new(_compiler, budget, _metrics, NullLogger<PageImageBuilder>.Instance);

        return new PageRenderer(
            storage,
            _compiler,
            compilations,
            new PageLibraries(new FakePageReadStore()),
            _metrics,
            budget,
            new PageHealth(render.AbandonedLimit),
            NullLogger<PageRenderer>.Instance);
    }

    private static DefaultHttpContext CreateContext(string? path) {
        return TestHttp.CreateContext(path, "GET", PagePath.Normalize(path).Key);
    }

    // No ContentLength, which is what a chunked request looks like to the endpoint.
    private static DefaultHttpContext ChunkedForm(string body) {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "POST", "/hello.cs");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context;
    }
}
