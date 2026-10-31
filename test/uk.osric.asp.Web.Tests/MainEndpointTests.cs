// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
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

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo($"{expectedKey} not found."));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo(expectedKey));
    }

    [TestCase("/Hello", "/hello")]
    [TestCase("/hello/", "/hello")]
    [TestCase("/a/../b", "/b")]
    [TestCase("/Hello world", "/hello%20world")]
    [TestCase("/Caf\u00e9", "/caf%C3%A9")]
    public async Task NonCanonicalPathRedirectsPermanentlyAsync(string requestPath, string expectedLocation) {
        DefaultHttpContext context = TestHttp.CreateContext(requestPath.TrimStart('/'), "GET", requestPath);
        FakeStorage storage = new();

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task StoredHandlerIsCompiledAndExecutedAsync() {
        DefaultHttpContext context = CreateContext("pages/hello.cs");
        FakeStorage storage = new();
        storage["/pages/hello.cs"] = new StoredPage(
            PagePath.Normalize("/pages/hello.cs"),
            "<% throw new System.InvalidOperationException(\"Template executed.\"); %>");

        InvalidOperationException? failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved));

        Assert.That(failure?.Message, Is.EqualTo("Template executed."));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo("/pages/hello.cs"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task SuccessfulTemplateIsRenderedAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "Hello, <% ctx.Write(\"world\"); %>!");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task StoredCompilationIsRenderedWithoutCompilingAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        TemplateCompiler compiler = new();
        CompileResult compiled = compiler.Compile(TemplateSourceGenerator.GenerateTemplateSource("Hello."));
        PageCompilation compilation = new(
            ContentFingerprint.Of("Hello."), TemplateCompiler.RuntimeId, compiled.Image, compiled.Diagnostics);
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "Hello.", compilation);
        int compiles = _compiler.CompileCount;

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task InvalidStoredSourceReturnsCompilationErrorAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakeStorage storage = new();
        storage["/broken.cs"] = new StoredPage(PagePath.Normalize("/broken.cs"), "<% int value = ; %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Handler failed to compile.").And.Contain("error CS"));
    }

    [Test]
    public async Task CompilationDiagnosticsAreHtmlEncodedAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakeStorage storage = new();
        storage["/broken.cs"] = new StoredPage(
            PagePath.Normalize("/broken.cs"), "<% int value = new System.Collections.Generic.List<int>(); %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(body, Does.Not.Contain("List<int>"));
        Assert.That(body, Does.Contain("List&lt;int&gt;"));
    }

    [Test]
    public void StorageFailurePropagatesAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakeStorage storage = new() { Failure = new InvalidOperationException("Storage unavailable.") };

        Assert.That(async () => await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Storage unavailable."));
    }

    [Test]
    public void RequestCancellationIsForwardedAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        FakeStorage storage = new();

        Assert.That(async () => await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(storage.LastCancellationToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public async Task StoredPageAtAReservedKeyIsNotFoundAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("site.css", "GET", "/site.css");
        FakeStorage storage = new();
        storage["/site.css"] = new StoredPage(PagePath.Normalize("/site.css"), "hijacked");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

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

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(201));
        Assert.That(context.Response.Headers["X-Page"].ToString(), Is.EqualTo("yes"));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("done"));
    }

    [Test]
    public async Task PageRedirectIsSentWithNoBodyAsync() {
        DefaultHttpContext context = CreateContext("moved.cs");
        FakeStorage storage = new();
        storage["/moved.cs"] = new StoredPage(PagePath.Normalize("/moved.cs"), "<% ctx.Redirect(\"/elsewhere\"); %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/elsewhere"));
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

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Osric/first"));
    }

    [Test]
    public async Task HeadRequestWritesNoBodyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs", "HEAD", "/hello.cs");
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "Hello, world!");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task OversizedRequestIsRejectedBeforeThePageRunsAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("form.cs", "POST", "/form.cs");
        context.Request.ContentLength = 2 * 1024 * 1024;
        FakeStorage storage = new();

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance, Reserved);

        Assert.That(context.Response.StatusCode, Is.EqualTo(413));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("too large"));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    private PageService CreatePages(IStorage storage) {
        return new PageService(storage, _compiler, _metrics, NullLogger<PageService>.Instance);
    }

    private static DefaultHttpContext CreateContext(string? path) {
        return TestHttp.CreateContext(path, "GET", PagePath.Normalize(path).Path);
    }
}
