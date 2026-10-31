// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

    private static readonly IWebHostEnvironment Production = new TestEnvironment("Production");
    private static readonly ReservedPaths Reserved = new(ReservedPaths.Default);
    private readonly RecordingCompiler _compiler = new(new TemplateCompiler());
    private readonly PageMetrics _metrics = new();

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

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

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

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task MissingPageShowsEmptyEditorAsync() {
        DefaultHttpContext context = CreateContext();
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("cols=\"50\"></textarea>"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task GetWithoutUsablePathReadsTheRootPageAsync(string? path) {
        DefaultHttpContext context = CreateContext(path: path);
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo("/"));
    }

    [Test]
    public async Task GetEncodesPathAndConsumesEncodedMessageAsync() {
        DefaultHttpContext context = CreateContext(path: "<page>&");
        context.Request.Headers.Cookie = "message=" + Uri.EscapeDataString("<script>saved</script>");

        await EditorEndpoint.Handler(context, CreatePages(new FakeStorage()), Production, Reserved);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("Editing /&lt;page&gt;&amp;"));
        Assert.That(body, Does.Contain("<p>&lt;script&gt;saved&lt;/script&gt;</p>"));
        Assert.That(context.Response.Headers.SetCookie.ToString(), Does.Contain("message=;"));
        Assert.That(context.Response.Headers.SetCookie.ToString(), Does.Contain("expires="));
    }

    [Test]
    public async Task GetWithoutMessageDoesNotSetCookieAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, CreatePages(new FakeStorage()), Production, Reserved);

        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
    }

    [TestCase("")]
    [TestCase("hello & <world>\n\u00e9")]
    public async Task PostSavesExactContentAndRedirectsAsync(string source) {
        DefaultHttpContext context = CreateContext("POST");
        SetContent(context, new StringValues(source));
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

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

        await EditorEndpoint.Handler(post, CreatePages(storage), Production, Reserved);

        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo("<% int value = ; %>"));
        Assert.That(storage["/page.cs"]?.Compilation?.Image, Is.Null);
        AssertRedirect(post, "Saved with errors.");

        DefaultHttpContext get = CreateContext();
        await EditorEndpoint.Handler(get, CreatePages(storage), Production, Reserved);

        string body = await TestHttp.ReadBodyAsync(get);
        Assert.That(body, Does.Contain("class=\"diagnostics\""));
        Assert.That(body, Does.Contain("error CS"));
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

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(storage.LastWritePath?.Path, Is.EqualTo("/"));
        Assert.That(storage["/"]?.Content, Is.EqualTo("replacement"));
        AssertRedirect(context, "Saved successfully.");
    }

    [TestCase("/edit/Hello", "/edit/hello")]
    [TestCase("/edit/hello/", "/edit/hello")]
    public async Task NonCanonicalEditorUrlRedirectsPermanentlyAsync(string requestPath, string expectedLocation) {
        DefaultHttpContext context = TestHttp.CreateContext(requestPath["/edit/".Length..], "GET", requestPath);
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task PostFromNonCanonicalUrlSavesTheCanonicalKeyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("Hello/", "POST", "/edit/Hello/");
        SetContent(context, new StringValues("source"));
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(storage.LastWritePath?.Path, Is.EqualTo("/hello"));
        Assert.That(storage["/hello"]?.Content, Is.EqualTo("source"));
        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/hello"));
    }

    [Test]
    public async Task MissingContentDoesNotOverwriteAsync() {
        DefaultHttpContext context = CreateContext("POST");
        Dictionary<string, StringValues> fields = [];
        context.Request.Form = new FormCollection(fields);
        FakeStorage storage = new();
        storage["/page.cs"] = new StoredPage(PagePath.Normalize("/page.cs"), "original");

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

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

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo("original"));
        AssertRedirect(context, "Content must have exactly one value.");
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public async Task StorageFailurePropagatesWithoutSuccessCookieAsync(string method) {
        DefaultHttpContext context = CreateContext(method);
        SetContent(context, new StringValues("replacement"));
        InvalidOperationException failure = new("Storage unavailable.");
        FakeStorage storage = new() { Failure = failure };

        InvalidOperationException? thrown = Assert.ThrowsAsync<InvalidOperationException>(() =>
            EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved));

        Assert.That(thrown?.Message, Is.EqualTo(failure.Message));

        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
        Assert.That(context.Response.Headers.Location, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public void CancellationReachesStorageAndPropagates(string method) {
        DefaultHttpContext context = CreateContext(method);
        SetContent(context, new StringValues("replacement"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        FakeStorage storage = new();

        Assert.CatchAsync<OperationCanceledException>(() =>
            EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved));

        Assert.That(storage.LastCancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(storage.WriteCount, Is.Zero);
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

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(storage["/page.cs"]?.Content, Is.EqualTo(expectedContent));
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
        AssertRedirect(context, message);
    }

    [TestCase("Production", true)]
    [TestCase("Development", false)]
    public async Task FlashCookieIsSecureOnlyInProductionAsync(string environment, bool secure) {
        DefaultHttpContext context = CreateContext("POST");
        SetContent(context, new StringValues("source"));

        await EditorEndpoint.Handler(context, CreatePages(new FakeStorage()), new TestEnvironment(environment), Reserved);

        string cookie = context.Response.Headers.SetCookie.ToString();
        Assert.That(cookie, Does.Contain("message="));
        Assert.That(cookie, Does.Contain("httponly"));
        Assert.That(cookie, Does.Contain("samesite=strict"));
        Assert.That(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase), Is.EqualTo(secure));
    }

    [Test]
    public async Task PostToAReservedKeyIsRefusedAndNothingIsStoredAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "POST", "/edit/site.css");
        SetContent(context, new StringValues("hijack"));
        FakeStorage storage = new();

        await EditorEndpoint.Handler(context, CreatePages(storage), Production, Reserved);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage["/site.css"], Is.Null);
        AssertRedirect(context, "/site.css is reserved by the site.");
    }

    private PageService CreatePages(IStorage storage) {
        return new PageService(storage, _compiler, _metrics, NullLogger<PageService>.Instance);
    }

    private static DefaultHttpContext CreateContext(string method = "GET", string? path = "page.cs") {
        return TestHttp.CreateContext(path, method, "/edit" + PagePath.Normalize(path).Path);
    }

    private static void SetContent(DefaultHttpContext context, StringValues content) {
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["content"] = content });
    }

    private static void AssertRedirect(DefaultHttpContext context, string message) {
        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(context.Request.Path.ToString()));
        Assert.That(context.Response.Headers.SetCookie.ToString(), Does.Contain("message=" + Uri.EscapeDataString(message)));
    }
}
