// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Reflection;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Endpoints;
using uk.osric.asp.Web.Pages;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class MainEndpointTests {

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

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance);

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

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance);

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

        TargetInvocationException? failure = Assert.ThrowsAsync<TargetInvocationException>(async () =>
            await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance));

        Assert.That(failure?.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(failure?.InnerException?.Message, Is.EqualTo("Template executed."));
        Assert.That(storage.LastReadPath?.Path, Is.EqualTo("/pages/hello.cs"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task SuccessfulTemplateIsRenderedAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakeStorage storage = new();
        storage["/hello.cs"] = new StoredPage(PagePath.Normalize("/hello.cs"), "Hello, <% ctx.Write(\"world\"); %>!");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance);

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

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance);

        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello."));
        Assert.That(_compiler.CompileCount, Is.EqualTo(compiles));
    }

    [Test]
    public async Task InvalidStoredSourceReturnsCompilationErrorAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakeStorage storage = new();
        storage["/broken.cs"] = new StoredPage(PagePath.Normalize("/broken.cs"), "<% int value = ; %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Handler failed to compile.").And.Contain("error CS"));
    }

    [Test]
    public async Task CompilationDiagnosticsAreHtmlEncodedAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        FakeStorage storage = new();
        storage["/broken.cs"] = new StoredPage(
            PagePath.Normalize("/broken.cs"), "<% int value = new System.Collections.Generic.List<int>(); %>");

        await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(body, Does.Not.Contain("List<int>"));
        Assert.That(body, Does.Contain("List&lt;int&gt;"));
    }

    [Test]
    public void StorageFailurePropagatesAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        FakeStorage storage = new() { Failure = new InvalidOperationException("Storage unavailable.") };

        Assert.That(async () => await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Storage unavailable."));
    }

    [Test]
    public void RequestCancellationIsForwardedAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        FakeStorage storage = new();

        Assert.That(async () => await MainEndpoint.Handle(context, CreatePages(storage), NullLogger<MainEndpoint>.Instance),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(storage.LastCancellationToken, Is.EqualTo(cancellation.Token));
    }

    private PageService CreatePages(IStorage storage) {
        return new PageService(storage, _compiler, _metrics, NullLogger<PageService>.Instance);
    }

    private static DefaultHttpContext CreateContext(string? path) {
        return TestHttp.CreateContext(path, "GET", PagePath.Normalize(path).Path);
    }
}
