// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageEndpointTests {

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);
    private static readonly RenderOptions Options = new();
    private static readonly PageStorageOptions StorageOptions = new();
    private static readonly PageFetchOptions FetchOptions = new();
    private readonly RecordingCompiler _compiler = new(new PageCompiler());
    private readonly PageMetrics _metrics = new();
    private readonly TestAntiforgery _antiforgery = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    private PageEnvironment EndpointEnvironment(RenderOptions? render = null, PageContentTypes? contentTypes = null) {
        return new PageEnvironment(
            Reserved,
            render ?? Options,
            contentTypes ?? TestContentTypes.Shipped,
            StorageOptions,
            FetchOptions,
            new FakePageStateStore(),
            new FakePageFetcher(),
            _metrics,
            _antiforgery);
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
        Assert.That(context.Response.Headers["Cache-Control"].ToString(), Is.EqualTo("no-cache"));
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
    public async Task AStoredScriptPageRevalidatesLikeAnyOtherPageAsync(string method) {
        FakePageStore storage = new();
        storage["/x.js"] = new PageTemplate(PagePath.Normalize("/x.js"), "export {};");
        DefaultHttpContext initial = TestHttp.CreateContext("x.js", method, "/x.js");

        await PageEndpoint.HandleAsync(initial, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        DefaultHttpContext conditional = TestHttp.CreateContext("x.js", method, "/x.js");
        conditional.Request.Headers.IfNoneMatch = initial.Response.Headers.ETag;

        await PageEndpoint.HandleAsync(conditional, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(initial.Response.ContentType, Is.EqualTo("text/javascript; charset=utf-8"));
        Assert.That(conditional.Response.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
        Assert.That(conditional.Response.ContentType, Is.Null);
        Assert.That(await TestHttp.ReadBodyAsync(conditional), Is.Empty);
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
        CompileResult compiled = compiler.Compile(TestTemplates.Generate("Hello."));
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
    public async Task MatchingIfNoneMatchReturnsBodylessNotModifiedAsync(string method) {
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");
        DefaultHttpContext initial = TestHttp.CreateContext("hello.cs", method, "/hello.cs");

        await PageEndpoint.HandleAsync(initial, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        DefaultHttpContext conditional = TestHttp.CreateContext("hello.cs", method, "/hello.cs");
        conditional.Request.Headers.IfNoneMatch = initial.Response.Headers.ETag;

        await PageEndpoint.HandleAsync(conditional, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

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

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task IfNoneMatchWildcardMatchesAnExistingPageAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "GET", "/hello.cs");
        context.Request.Headers.IfNoneMatch = "*";
        FakePageStore storage = new();
        storage["/hello.cs"] = new PageTemplate(PagePath.Normalize("/hello.cs"), "Hello, world!");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
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
        RenderOptions options = new() { TimeoutSeconds = 0.25 };

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
        RenderOptions options = new() { MaxBodyChars = 5 };

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
        context.Request.Headers.Cookie = ".uk.osric.asp.auth=forged";
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
    public async Task AnAnonymousPageStillCarriesItsValidatorAsync() {
        DefaultHttpContext context = CreateContext("public.cs");
        FakePageStore storage = new();
        storage["/public.cs"] = new PageTemplate(PagePath.Normalize("/public.cs"), "Hello.");

        await PageEndpoint.HandleAsync(context, CreatePages(storage), NullLoggerFactory.Instance, EndpointEnvironment());

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.Headers.ETag, Is.Not.Empty);
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

    // No ContentLength, which is what a chunked request looks like to the endpoint.
    private static DefaultHttpContext ChunkedForm(string body) {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "POST", "/hello.cs");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        return context;
    }
}
