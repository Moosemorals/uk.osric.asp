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
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Endpoints;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class EditorEndpointTests {

    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);
    private readonly RecordingCompiler _compiler = new(new TemplateCompiler());
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
        FakeStorage storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), source);

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(body, Does.Contain($"<textarea name=\"content\" rows=\"10\" cols=\"50\">{WebUtility.HtmlEncode(source)}</textarea>"));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo("/page.cs"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task GetDoesNotCompileThePageAsync() {
        DefaultHttpContext context = CreateContext();
        FakeStorage storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "uncompiled");
        int compiles = _compiler.CompileCount;

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task MissingPageShowsEmptyEditorAsync() {
        DefaultHttpContext context = CreateContext();
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("cols=\"50\"></textarea>"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task GetWithoutUsablePathReadsTheRootPageAsync(string? path) {
        DefaultHttpContext context = CreateContext(path: path);
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo("/"));
    }

    [Test]
    public async Task GetEncodesPathAndConsumesTheSessionMessageAsync() {
        DefaultHttpContext context = CreateContext(path: "<page>&");
        TestSession session = Session(context);
        session.SetString(EditorEndpoint.MessageKey, "<script>saved</script>");

        await EditorEndpoint.Handler(context, CreatePages(new FakeStorage()), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("Editing /&lt;page&gt;&amp;"));
        Assert.That(body, Does.Contain("<p>&lt;script&gt;saved&lt;/script&gt;</p>"));
        Assert.That(session.TryGetValue(EditorEndpoint.MessageKey, out _), Is.False);
    }

    [Test]
    public async Task GetWithoutMessageSetsNoCookieAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, CreatePages(new FakeStorage()), Reserved, _antiforgery);

        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
    }

    [Test]
    public async Task GetRendersAnAntiforgeryTokenAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, CreatePages(new FakeStorage()), Reserved, _antiforgery);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("name=\"__RequestVerificationToken\""));
        Assert.That(body, Does.Contain("value=\"request-token\""));
        Assert.That(_antiforgery.TokenRequests, Is.EqualTo(1));
    }

    [Test]
    public async Task GetDoesNotCacheTheSourceAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, CreatePages(new FakeStorage()), Reserved, _antiforgery);

        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task PostWithInvalidAntiforgeryWritesNothingAsync() {
        DefaultHttpContext context = CreateContext("POST");
        context.Features.Set<IAntiforgeryValidationFeature>(new TestAntiforgeryValidationFeature(false));
        SetContent(context, new StringValues("hijack"));
        FakeStorage storage = new();

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
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.EqualTo(1));
        Assert.That(storage.LastWritePath?.Path, Is.EqualTo("/page.cs"));
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
        FakeStorage storage = new();

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
        FakeStorage storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "line one\nline two", new PageCompilation(
            ContentFingerprint.Of("line one\nline two"),
            "1:build",
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
        FakeStorage storage = new();

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
        FakeStorage storage = new();
        storage["/"] = new StoredPage(PagePath.Normalize("/"), "original");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.LastWritePath?.Path, Is.EqualTo("/"));
        Assert.That(storage["/"]?.Content, Is.EqualTo("replacement"));
        AssertRedirect(context, "Saved successfully.");
    }

    [TestCase("/edit/Hello", "", "/edit/hello")]
    [TestCase("/edit/hello/", "", "/edit/hello")]
    [TestCase("/edit/Hello", "?tab=source", "/edit/hello?tab=source")]
    public async Task NonCanonicalEditorUrlRedirectsPermanentlyAsync(string requestPath, string query, string expectedLocation) {
        DefaultHttpContext context = TestHttp.CreateContext(requestPath["/edit/".Length..], "GET", requestPath);
        context.Request.QueryString = new QueryString(query);
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task PostFromNonCanonicalUrlSavesTheCanonicalKeyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("Hello/", "POST", "/edit/Hello/");
        SetContent(context, new StringValues("source"));
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.LastWritePath?.Path, Is.EqualTo("/hello"));
        Assert.That(storage["/hello"]?.Content, Is.EqualTo("source"));
        Assert.That(context.Response.StatusCode, Is.EqualTo(303));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/hello"));
    }

    [Test]
    public async Task MissingContentDoesNotOverwriteAsync() {
        DefaultHttpContext context = CreateContext("POST");
        Dictionary<string, StringValues> fields = [];
        context.Request.Form = new FormCollection(fields);
        FakeStorage storage = new();
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
        FakeStorage storage = new();
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
        FakeStorage storage = new() { Failure = failure };

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
        FakeStorage storage = new();

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
        FakeStorage storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "original");

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo(expectedContent));
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
        AssertRedirect(context, message);
    }

    [Test]
    public async Task PostToAReservedKeyIsRefusedAndNothingIsStoredAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "POST", "/edit/site.css");
        SetContent(context, new StringValues("hijack"));
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Reserved, _antiforgery);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/site.css"], Is.Null);
        AssertRedirect(context, "/site.css is reserved by the site.");
    }

    private PageService CreatePages(IStorage storage) {
        return new PageService(
            storage,
            _compiler,
            _metrics,
            new PageBudget(new RenderOptions(), new CompileOptions()),
            new PageHealth(RenderOptions.DefaultAbandonedLimit),
            NullLogger<PageService>.Instance);
    }

    private static DefaultHttpContext CreateContext(string method = "GET", string? path = "page.cs") {
        return TestHttp.CreateContext(path, method, "/edit" + PagePath.Normalize(path).Path);
    }

    private static void SetContent(DefaultHttpContext context, StringValues content) {
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["content"] = content });
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
