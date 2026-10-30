// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.WebTests;

using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using System.Reflection;
using uk.osric.asp.Web;

[TestFixture]
public sealed class MainEndpointTests {
    [TestCase(null)]
    [TestCase("")]
    [TestCase("missing.cs")]
    public async Task MissingPageReturnsNotFoundAsync(string? path) {
        DefaultHttpContext context = CreateContext(path);
        PageStorage storage = new(null);

        await MainEndpoint.Handle(context, storage);

        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
        Assert.That(await ReadBodyAsync(context), Is.EqualTo("File not found."));
        Assert.That(storage.ReadPath, Is.EqualTo(string.IsNullOrEmpty(path) ? null : path));
    }

    [Test]
    public async Task StoredHandlerIsCompiledAndExecutedAsync() {
        DefaultHttpContext context = CreateContext("pages/hello.cs");
        PageStorage storage = new("""
            using uk.osric.asp.Core;
            public sealed class Page : IHandler {
                public void Handle(IContext context) {
                    throw new System.InvalidOperationException("Stored handler executed.");
                }
            }
            """);

        TargetInvocationException? failure = Assert.ThrowsAsync<TargetInvocationException>(async () => await MainEndpoint.Handle(context, storage));

        Assert.That(failure?.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(failure?.InnerException?.Message, Is.EqualTo("Stored handler executed."));
        Assert.That(storage.ReadPath, Is.EqualTo("pages/hello.cs"));
        Assert.That(await ReadBodyAsync(context), Is.Empty);
    }

    [Test]
    public async Task SuccessfulStoredHandlerReturnsSuccessPageAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        PageStorage storage = new("""
            using uk.osric.asp.Core;
            public sealed class Page : IHandler {
                public void Handle(IContext context) { }
            }
            """);

        await MainEndpoint.Handle(context, storage);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(await ReadBodyAsync(context), Does.Contain("Handler executed successfully."));
    }

    [Test]
    public async Task InvalidStoredSourceReturnsCompilationErrorAsync() {
        DefaultHttpContext context = CreateContext("broken.cs");
        PageStorage storage = new("public class Broken {");

        await MainEndpoint.Handle(context, storage);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(await ReadBodyAsync(context), Does.Contain("Handler failed to compile.").And.Contain("CS1513"));
    }

    [Test]
    public void CompiledSourceWithoutHandlerPropagatesFailureAsync() {
        DefaultHttpContext context = CreateContext("no-handler.cs");
        PageStorage storage = new("public class NotAHandler { }");

        Assert.That(async () => await MainEndpoint.Handle(context, storage),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("No handler found in the assembly."));
    }

    [Test]
    public void StorageFailurePropagatesAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        PageStorage storage = new(null) { Failure = new InvalidOperationException("Storage unavailable.") };

        Assert.That(async () => await MainEndpoint.Handle(context, storage),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Storage unavailable."));
    }

    [Test]
    public void RequestCancellationIsForwardedAsync() {
        DefaultHttpContext context = CreateContext("hello.cs");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        PageStorage storage = new(null);

        Assert.That(async () => await MainEndpoint.Handle(context, storage), Throws.InstanceOf<OperationCanceledException>());

        Assert.That(storage.LastCancellationToken, Is.EqualTo(cancellation.Token));
    }

    private static DefaultHttpContext CreateContext(string? path) {
        DefaultHttpContext context = new();
        context.Request.RouteValues["path"] = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadBodyAsync(DefaultHttpContext context) {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
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
