// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Net;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class EditorEndpointTests {

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);
    private readonly RecordingCompiler _compiler = new(new PageCompiler());
    private readonly PageMetrics _metrics = new();
    private readonly TestAntiforgery _antiforgery = new();

    [TearDown]
    public void StopMetrics() {
        _metrics.Dispose();
    }

    [TestCase("plain text")]
    [TestCase("&amp; <tag> \"quoted\"")]
    [TestCase("</textarea><script>alert('source')</script>")]
    [TestCase("line one\nline two: \u00e9")]
    public async Task GetEncodesStoredSourceAsync(string source) {
        DefaultHttpContext context = CreateContext();
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), source);

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(body, Does.Contain($"<textarea name=\"content\" rows=\"10\" cols=\"50\">{WebUtility.HtmlEncode(source)}</textarea>"));
        Assert.That(storage.LastReadPath?.Key, Is.EqualTo("/page.cs"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task GetDoesNotCompileThePageAsync() {
        DefaultHttpContext context = CreateContext();
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "uncompiled");
        int compiles = _compiler.CompileCount;

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task MissingPageShowsEmptyEditorAsync() {
        DefaultHttpContext context = CreateContext();
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("cols=\"50\"></textarea>"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task GetWithoutUsablePathReadsTheRootPageAsync(string? path) {
        DefaultHttpContext context = CreateContext(path: path);
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(storage.LastReadPath?.Key, Is.EqualTo("/"));
    }

    [Test]
    public async Task GetEncodesPathAndConsumesTheSessionMessageAsync() {
        DefaultHttpContext context = CreateContext(path: "<page>&");
        TestSession session = Session(context);
        session.SetString(EditorEndpoint.MessageKey, "<script>saved</script>");

        await EditorEndpoint.Handler(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("Editing /&lt;page&gt;&amp;"));
        Assert.That(body, Does.Contain("<p>&lt;script&gt;saved&lt;/script&gt;</p>"));
        Assert.That(session.TryGetValue(EditorEndpoint.MessageKey, out _), Is.False);
    }

    [Test]
    public async Task GetWithoutMessageSetsNoCookieAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
    }

    [Test]
    public async Task GetRendersAnAntiforgeryTokenAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
        Assert.That(body, Does.Contain("value=\"request-token\""));
        Assert.That(_antiforgery.TokenRequests, Is.EqualTo(1));
    }

    [Test]
    public async Task GetDoesNotCacheTheSourceAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [TestCase("?cmd=")]
    [TestCase("?cmd=unknown")]
    [TestCase("?cmd=delete&cmd=scrub")]
    public async Task InvalidCommandReturnsBadRequestWithoutReadingOrWritingAsync(string query) {
        DefaultHttpContext context = CreateContext();
        context.Request.QueryString = new QueryString(query);
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        Assert.That(storage.ReadCount, Is.Zero);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task CanonicalRedirectDoesNotCacheTheEditorUrlAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("Hello", "GET", "/edit/Hello");
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task ListingUsesOrdinalHeadSummariesAndSeparatesDeletedPagesAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/edit");
        FakePageStore storage = new();
        storage["/zeta"] = new StoredPage(PagePath.Normalize("/zeta"), "not loaded");
        storage["/a&b"] = new StoredPage(PagePath.Normalize("/a&b"), "not loaded");
        storage["/gone"] = new StoredPage(PagePath.Normalize("/gone"), "retained");
        await storage.DeletePageAsync(PagePath.Normalize("/gone"));

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(body.IndexOf("/a&amp;b", StringComparison.Ordinal), Is.LessThan(body.IndexOf("/zeta", StringComparison.Ordinal)));
        Assert.That(body, Does.Contain("Deleted pages"));
        Assert.That(body, Does.Contain("/gone (history retained)"));
        Assert.That(body, Does.Not.Contain("href=\"/gone\""));
        Assert.That(storage.ReadCount, Is.Zero);
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task NewPageNormalizesAndRedirectsWithoutSavingAsync() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/edit");
        context.Request.QueryString = new QueryString("?cmd=new");
        SetField(context, "path", new StringValues("Hello//World"));
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/hello/world"));
        Assert.That(storage.ReadCount, Is.Zero);
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task GeneratedViewEncodesSourceAndDoesNotCompileOrWriteAsync() {
        const string source = "<% ctx.Write(\"ok\"); %>";
        DefaultHttpContext context = CreateContext();
        context.Request.QueryString = new QueryString("?cmd=generated");
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), source, new PageCompilation(
            ContentFingerprint.Of(source), "old-runtime", null, null,
            [new PageDiagnostic(PageDiagnosticSeverity.Error, null, "stored compile failure")]));
        int compiles = _compiler.CompileCount;

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(body, Does.Contain("Generated from the saved template by the current runtime"));
        Assert.That(body, Does.Contain("ctx.Write(&quot;ok&quot;);"));
        Assert.That(body, Does.Contain("#line 1 &quot;page&quot;"));
        Assert.That(body, Does.Not.Contain("stored compile failure"));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task GeneratedViewShowsTemplateDiagnosticWithoutPartialSourceAsync() {
        DefaultHttpContext context = CreateContext();
        context.Request.QueryString = new QueryString("?cmd=generated");
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "before<% if (true) {");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("TEMPLATE001"));
        Assert.That(body, Does.Not.Contain("class GeneratedPage"));
        Assert.That(body, Does.Not.Contain("style=\"max-width:100%;overflow-x:auto\""));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task HistoryListsRevisionsAndShowsSelectedSavedSourceWithoutCompilingAsync() {
        PagePath path = PagePath.Normalize("/page.cs");
        FakePageStore storage = new();
        StoredPage first = new(path, "first <source>");
        StoredPage second = new(path, "second source");
        await storage.SavePageAsync(first);
        await storage.SavePageAsync(second);

        DefaultHttpContext history = CreateContext();
        history.Request.QueryString = new QueryString("?cmd=history");
        await EditorEndpoint.Handler(history, CreatePages(storage), Reserved, _antiforgery);

        string historyBody = await TestHttp.ReadBodyAsync(history);
        Assert.That(history.Response.StatusCode, Is.EqualTo(200));
        Assert.That(historyBody, Does.Contain(first.Hash));
        Assert.That(historyBody, Does.Contain(second.Hash));
        Assert.That(historyBody, Does.Contain("<strong>Current</strong>"));
        Assert.That(historyBody.IndexOf(second.Hash, StringComparison.Ordinal), Is.LessThan(historyBody.IndexOf(first.Hash, StringComparison.Ordinal)));
        Assert.That(historyBody, Does.Not.Contain("first &lt;source&gt;"));
        Assert.That(history.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));

        DefaultHttpContext revision = CreateContext();
        revision.Request.QueryString = new QueryString($"?cmd=revision&hash={first.Hash}");
        int compiles = _compiler.CompileCount;
        await EditorEndpoint.Handler(revision, CreatePages(storage), Reserved, _antiforgery);

        string revisionBody = await TestHttp.ReadBodyAsync(revision);
        Assert.That(revision.Response.StatusCode, Is.EqualTo(200));
        Assert.That(revisionBody, Does.Contain("first &lt;source&gt;"));
        Assert.That(revisionBody, Does.Not.Contain("second source"));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
        Assert.That(storage.WriteCount, Is.EqualTo(2));
        Assert.That(revision.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task DeleteAndScrubConfirmationsDoNotMutateOnGetAsync() {
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "saved");
        DefaultHttpContext delete = CreateContext();
        delete.Request.QueryString = new QueryString("?cmd=delete");

        await EditorEndpoint.Handler(delete, CreatePages(storage), Reserved, _antiforgery);

        string deleteBody = await TestHttp.ReadBodyAsync(delete);
        Assert.That(deleteBody, Does.Contain("retains its source and revision history"));
        PageHeadSummary? liveHead = await storage.GetHeadAsync(PagePath.Normalize("/page.cs"));
        Assert.That(liveHead?.IsDeleted, Is.False);

        DefaultHttpContext scrub = CreateContext();
        scrub.Request.QueryString = new QueryString("?cmd=scrub");
        await EditorEndpoint.Handler(scrub, CreatePages(storage), Reserved, _antiforgery);

        string scrubBody = await TestHttp.ReadBodyAsync(scrub);
        Assert.That(scrubBody, Does.Contain("cannot be undone"));
        Assert.That(scrubBody, Does.Contain("Rotate exposed secrets"));
        Assert.That(scrubBody, Does.Contain("name=\"confirmation\""));
        StoredPage? revision = await storage.GetRevisionByHashAsync(storage["/page.cs"]!.Hash);
        Assert.That(revision, Is.Not.Null);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ScrubRequiresExactCanonicalPathAndDoesNotLeakCleanupErrorsAsync() {
        DefaultHttpContext context = CreateContext("POST");
        context.Request.QueryString = new QueryString("?cmd=scrub");
        SetField(context, "confirmation", new StringValues("/PAGE.CS"));
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "sensitive-source");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(await storage.GetHeadAsync(PagePath.Normalize("/page.cs")), Is.Not.Null);

        DefaultHttpContext failed = CreateContext("POST");
        failed.Request.QueryString = new QueryString("?cmd=scrub");
        SetField(failed, "confirmation", new StringValues("/page.cs"));
        FakePageStore unavailable = new() { Failure = new InvalidOperationException("secret source leaked") };

        await EditorEndpoint.Handler(failed, CreatePages(unavailable), Reserved, _antiforgery);

        string failureBody = await TestHttp.ReadBodyAsync(failed);
        Assert.That(failed.Response.StatusCode, Is.EqualTo(503));
        Assert.That(failureBody, Does.Not.Contain("secret source leaked"));
        Assert.That(failed.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task PostWithInvalidAntiforgeryWritesNothingAsync() {
        DefaultHttpContext context = CreateContext("POST");
        context.Features.Set<IAntiforgeryValidationFeature>(new TestAntiforgeryValidationFeature(false));
        SetContent(context, new StringValues("hijack"));
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [TestCase("")]
    [TestCase("hello & <world>\n\u00e9")]
    public async Task PostSavesExactContentAndRedirectsAsync(string source) {
        DefaultHttpContext context = CreateContext("POST");
        SetContent(context, new StringValues(source));
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.EqualTo(1));
        Assert.That(storage.LastWritePath?.Key, Is.EqualTo("/page.cs"));
        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo(source));
        Assert.That(storage["/page.cs"]?.Compilation, Is.Not.Null);
        Assert.That(storage.ReadCount, Is.Zero);
        AssertRedirect(context, "Saved successfully.");
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task PostKeepsTheSourceAndShowsDiagnosticsWhenThePageDoesNotCompileAsync() {
        DefaultHttpContext post = CreateContext("POST");
        SetContent(post, new StringValues("<% int value = ; %>"));
        FakePageStore storage = new();

        await EditorEndpoint.Handler(post, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo("<% int value = ; %>"));
        Assert.That(storage["/page.cs"]?.Compilation?.Image, Is.Null);
        AssertRedirect(post, "Saved with errors.");

        DefaultHttpContext get = CreateContext();
        await EditorEndpoint.Handler(get, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(get);
        Assert.That(body, Does.Contain("class=\"diagnostics\""));
        Assert.That(body, Does.Contain("error CS"));
        Assert.That(body, Does.Contain("diagnostic-error"));
    }

    [Test]
    public async Task DiagnosticsAreShownInPositionOrderAsync() {
        DefaultHttpContext get = CreateContext();
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "line one\nline two", new PageCompilation(
            ContentFingerprint.Of("line one\nline two"),
            "1:build",
            null,
            null,
            [
                new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(2, 5), "CS0103: later line"),
                new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(1, 9), "CS1002: earlier line"),
            ]));

        await EditorEndpoint.Handler(get, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(get);
        Assert.That(body.IndexOf("CS1002", StringComparison.Ordinal), Is.LessThan(body.IndexOf("CS0103", StringComparison.Ordinal)));
    }

    [Test]
    public async Task PostWithAWarningSaysSavedWithWarningsAsync() {
        DefaultHttpContext post = CreateContext("POST");
        SetContent(post, new StringValues("<% int unused = 1; %>"));
        FakePageStore storage = new();

        await EditorEndpoint.Handler(post, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage["/page.cs"]?.Compilation?.Image, Is.Not.Null.And.Not.Empty);
        AssertRedirect(post, "Saved with warnings.");

        DefaultHttpContext get = CreateContext();
        await EditorEndpoint.Handler(get, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(get);
        Assert.That(body, Does.Contain("warning CS0219"));
        Assert.That(body, Does.Contain("diagnostic-warning"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/")]
    public async Task PostWithoutUsablePathWritesTheRootPageAsync(string? path) {
        DefaultHttpContext context = CreateContext("POST", path);
        SetContent(context, new StringValues("replacement"));
        FakePageStore storage = new();
        storage["/"] = new StoredPage(PagePath.Normalize("/"), "original");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.LastWritePath?.Key, Is.EqualTo("/"));
        Assert.That(storage["/"]?.Content, Is.EqualTo("replacement"));
        AssertRedirect(context, "Saved successfully.");
    }

    [TestCase("/edit/Hello", "", "/edit/hello")]
    [TestCase("/edit/hello/", "", "/edit/hello")]
    [TestCase("/edit/Hello", "?tab=source", "/edit/hello?tab=source")]
    public async Task NonCanonicalEditorUrlRedirectsPermanentlyAsync(string requestPath, string query, string expectedLocation) {
        DefaultHttpContext context = TestHttp.CreateContext(requestPath["/edit/".Length..], "GET", requestPath);
        context.Request.QueryString = new QueryString(query);
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public async Task AKeyWithADelimiterIsNotFoundAsync(string method) {
        DefaultHttpContext context = TestHttp.CreateContext("hello?world", method, "/edit/hello?world");
        SetContent(context, new StringValues("source"));
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("/hello?world not found."));
        Assert.That(storage.ReadCount, Is.Zero);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task PostFromNonCanonicalUrlSavesTheCanonicalKeyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("Hello/", "POST", "/edit/Hello/");
        SetContent(context, new StringValues("source"));
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.LastWritePath?.Key, Is.EqualTo("/hello"));
        Assert.That(storage["/hello"]?.Content, Is.EqualTo("source"));
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/hello"));
    }

    [Test]
    public async Task MissingContentDoesNotOverwriteAsync() {
        DefaultHttpContext context = CreateContext("POST");
        Dictionary<string, StringValues> fields = [];
        context.Request.Form = new FormCollection(fields);
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "original");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo("original"));
        AssertRedirect(context, "Content is required.");
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task InvalidContentCountDoesNotOverwriteAsync(int count) {
        DefaultHttpContext context = CreateContext("POST");
        SetContent(context, new StringValues([.. Enumerable.Repeat("replacement", count)]));
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "original");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo("original"));
        AssertRedirect(context, "Content must have exactly one value.");
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public async Task StorageFailurePropagatesWithoutARedirectOrMessageAsync(string method) {
        DefaultHttpContext context = CreateContext(method);
        SetContent(context, new StringValues("replacement"));
        InvalidOperationException failure = new("Storage unavailable.");
        FakePageStore storage = new() { Failure = failure };

        InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery));

        Assert.That(thrown?.Message, Is.EqualTo(failure.Message));

        Assert.That(Session(context).Keys, Is.Empty);
        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
        Assert.That(context.Response.Headers.Location, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public async Task CancellationPropagatesAndStoresNothingAsync(string method) {
        DefaultHttpContext context = CreateContext(method);
        SetContent(context, new StringValues("replacement"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        FakePageStore storage = new();

        await Assert.CatchAsync<OperationCanceledException>(() =>
            EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery));

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(Session(context).Keys, Is.Empty);
        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
    }

    [TestCase("content=hello+%26+%3Cworld%3E%0A%C3%A9", "hello & <world>\n\u00e9", "Saved successfully.", 1)]
    [TestCase("content=", "", "Saved successfully.", 1)]
    [TestCase("other=value", "original", "Content is required.", 0)]
    [TestCase("content=first&content=second", "original", "Content must have exactly one value.", 0)]
    public async Task FormBodyIsDecodedAndValidatedAsync(string body, string expectedContent, string message, int writes) {
        DefaultHttpContext context = CreateContext("POST");
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        FakePageStore storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "original");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo(expectedContent));
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
        AssertRedirect(context, message);
    }

    [Test]
    public async Task PostToAReservedKeyIsRefusedAndNothingIsStoredAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("favicon.ico", "POST", "/edit/favicon.ico");
        SetContent(context, new StringValues("hijack"));
        FakePageStore storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/favicon.ico"], Is.Null);
        AssertRedirect(context, "/favicon.ico is reserved by the site.");
    }

    [TestCase("/space name", "/space%20name")]
    [TestCase("/literal%2fvalue", "/literal%252fvalue")]
    [TestCase("/a&b", "/a%26b")]
    public async Task ListingEncodesUrlPathsSeparatelyFromLabelsAsync(string key, string url) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/edit");
        FakePageStore storage = new();
        storage[key] = new StoredPage(PagePath.Normalize(key), "source");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain($"href=\"{url}\""));
        Assert.That(body, Does.Contain($"href=\"/edit{url}?cmd=scrub\""));
    }

    [TestCase("space name", "/edit/space%20name")]
    [TestCase("literal%2fvalue", "/edit/literal%252fvalue")]
    public async Task NewPageRedirectEncodesLiteralPathCharactersAsync(string key, string url) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "POST", "/edit");
        context.Request.QueryString = new QueryString("?cmd=new");
        SetField(context, "path", new StringValues(key));

        await EditorEndpoint.Handler(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(url));
    }

    [TestCase("space name")]
    [TestCase("literal%2fvalue")]
    public async Task CanonicalDecodedRequestPathDoesNotRedirectAsync(string key) {
        DefaultHttpContext context = CreateContext(path: key);

        await EditorEndpoint.Handler(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain($"href=\"{WebUtility.HtmlEncode("/" + Uri.EscapeDataString(key))}\""));
    }

    [Test]
    public async Task MissingHeadStillOffersScrubCleanupRetryAsync() {
        DefaultHttpContext context = CreateContext();
        context.Request.QueryString = new QueryString("?cmd=scrub");

        await EditorEndpoint.Handler(context, CreatePages(new FakePageStore()), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("name=\"confirmation\""));
    }

    private PageEditor CreatePages(IPageStore storage) {
        PageBudget budget = new(new RenderOptions(), new CompileOptions());
        TemplateCompiler templates = new(_compiler, budget, _metrics, NullLogger<TemplateCompiler>.Instance);

        return new PageEditor(storage, templates, _metrics, NullLogger<PageEditor>.Instance);
    }

    private static DefaultHttpContext CreateContext(string method = "GET", string? path = "page.cs") {
        return TestHttp.CreateContext(path, method, "/edit" + PagePath.Normalize(path).Key);
    }

    private static void SetContent(DefaultHttpContext context, StringValues content) {
        SetField(context, "content", content);
    }

    private static void SetField(DefaultHttpContext context, string name, StringValues value) {
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> { [name] = value });
    }

    private static TestSession Session(DefaultHttpContext context) {
        return (TestSession)context.Features.Get<ISessionFeature>()!.Session;
    }

    private static void AssertRedirect(DefaultHttpContext context, string message) {
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(context.Request.Path.ToString()));
        Assert.That(Session(context).GetString(EditorEndpoint.MessageKey), Is.EqualTo(message));
    }
}
