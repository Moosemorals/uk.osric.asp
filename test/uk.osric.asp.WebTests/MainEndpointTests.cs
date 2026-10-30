// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.WebTests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Reflection;
using uk.osric.asp.Web;

[TestFixture]
public sealed class MainEndpointTests {
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("missing.cs")]
    public async Task MissingPageReturnsNotFoundAsync(string? path) {
        DefaultHttpContext context = TestHttp.CreateContext(path);
        PageStorage storage = new(null);

        await MainEndpoint.Handle(context, storage, NullLogger<MainEndpoint>.Instance);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("File not found."));
        Assert.That(storage.ReadPath, Is.EqualTo(string.IsNullOrWhiteSpace(path) ? null : path));
    }

    [Test]
    public async Task StoredHandlerIsCompiledAndExecutedAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("pages/hello.cs");
        PageStorage storage = new("<% throw new System.InvalidOperationException(\"Template executed.\"); %>");

        TargetInvocationException? failure = Assert.ThrowsAsync<TargetInvocationException>(async () =>
            await MainEndpoint.Handle(context, storage, NullLogger<MainEndpoint>.Instance));

        Assert.That(failure?.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(failure?.InnerException?.Message, Is.EqualTo("Template executed."));
        Assert.That(storage.ReadPath, Is.EqualTo("pages/hello.cs"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task SuccessfulTemplateIsRenderedAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs");
        PageStorage storage = new("Hello, <% ctx.Write(\"world\"); %>!");

        await MainEndpoint.Handle(context, storage, NullLogger<MainEndpoint>.Instance);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.EqualTo("Hello, world!"));
    }

    [Test]
    public async Task InvalidStoredSourceReturnsCompilationErrorAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("broken.cs");
        PageStorage storage = new("<% int value = ; %>");

        await MainEndpoint.Handle(context, storage, NullLogger<MainEndpoint>.Instance);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("Handler failed to compile.").And.Contain("error CS"));
    }

    [Test]
    public async Task CompilationDiagnosticsAreHtmlEncodedAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("broken.cs");
        PageStorage storage = new("<% int value = new System.Collections.Generic.List<int>(); %>");

        await MainEndpoint.Handle(context, storage, NullLogger<MainEndpoint>.Instance);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(body, Does.Not.Contain("List<int>"));
        Assert.That(body, Does.Contain("List&lt;int&gt;"));
    }

    [Test]
    public void StorageFailurePropagatesAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs");
        PageStorage storage = new(null) { Failure = new InvalidOperationException("Storage unavailable.") };

        Assert.That(async () => await MainEndpoint.Handle(context, storage, NullLogger<MainEndpoint>.Instance),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Storage unavailable."));
    }

    [Test]
    public void RequestCancellationIsForwardedAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("hello.cs");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        PageStorage storage = new(null);

        Assert.That(async () => await MainEndpoint.Handle(context, storage, NullLogger<MainEndpoint>.Instance),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(storage.LastCancellationToken, Is.EqualTo(cancellation.Token));
    }

    private sealed class PageStorage(string? content) : IStorage {
        public string? ReadPath { get; private set; }
        public Exception? Failure { get; init; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IEnumerable<string>> ListFilesAsync(CancellationToken cancellationToken = default) {
            throw new NotSupportedException();
        }

        public Task<string?> GetFileContentsAsync(string path, CancellationToken cancellationToken = default) {
            ReadPath = path;
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) {
                throw Failure;
            }
            return Task.FromResult(content);
        }

        public Task SetFileContentsAsync(string path, string source, CancellationToken cancellationToken = default) {
            throw new NotSupportedException();
        }
    }
}
