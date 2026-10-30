// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.WebTests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Net;
using System.Text;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Endpoints;

[TestFixture]
public sealed class EditorEndpointTests {
    [TestCase("plain text")]
    [TestCase("&amp; <tag> \"quoted\"")]
    [TestCase("</textarea><script>alert('source')</script>")]
    [TestCase("line one\nline two: \u00e9")]
    public async Task GetEncodesStoredSourceAsync(string source) {
        DefaultHttpContext context = CreateContext();
        RecordingStorage storage = new() { Content = source };

        await EditorEndpoint.Handler(context, storage);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(body, Does.Contain($"<textarea name=\"content\" rows=\"10\" cols=\"50\">{WebUtility.HtmlEncode(source)}</textarea>"));
        Assert.That(storage.ReadPath, Is.EqualTo("page.cs"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task MissingPageShowsEmptyEditorAsync() {
        DefaultHttpContext context = CreateContext();
        RecordingStorage storage = new();

        await EditorEndpoint.Handler(context, storage);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("cols=\"50\"></textarea>"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task GetWithoutUsablePathDoesNotReadStorageAsync(string? path) {
        DefaultHttpContext context = CreateContext(path: path);
        RecordingStorage storage = new();

        await EditorEndpoint.Handler(context, storage);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task GetEncodesPathAndConsumesEncodedMessageAsync() {
        DefaultHttpContext context = CreateContext(path: "<page>&");
        context.Request.Headers.Cookie = "message=" + Uri.EscapeDataString("<script>saved</script>");

        await EditorEndpoint.Handler(context, new RecordingStorage());

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("Editing &lt;page&gt;&amp;"));
        Assert.That(body, Does.Contain("<p>&lt;script&gt;saved&lt;/script&gt;</p>"));
        Assert.That(context.Response.Headers.SetCookie.ToString(), Does.Contain("message=;"));
        Assert.That(context.Response.Headers.SetCookie.ToString(), Does.Contain("expires="));
    }

    [Test]
    public async Task GetWithoutMessageDoesNotSetCookieAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, new RecordingStorage());

        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
    }

    [TestCase("")]
    [TestCase("hello & <world>\n\u00e9")]
    public async Task PostSavesExactContentAndRedirectsAsync(string source) {
        DefaultHttpContext context = CreateContext("POST");
        SetContent(context, new StringValues(source));
        RecordingStorage storage = new();

        await EditorEndpoint.Handler(context, storage);

        Assert.That(storage.WriteCount, Is.EqualTo(1));
        Assert.That(storage.WritePath, Is.EqualTo("page.cs"));
        Assert.That(storage.Content, Is.EqualTo(source));
        Assert.That(storage.ReadCount, Is.Zero);
        AssertRedirect(context, "Saved successfully.");
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task PostWithoutUsablePathDoesNotOverwriteAsync(string? path) {
        DefaultHttpContext context = CreateContext("POST", path);
        SetContent(context, new StringValues("replacement"));
        RecordingStorage storage = new() { Content = "original" };

        await EditorEndpoint.Handler(context, storage);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage.Content, Is.EqualTo("original"));
        AssertRedirect(context, "Path is required.");
    }

    [Test]
    public async Task MissingContentDoesNotOverwriteAsync() {
        DefaultHttpContext context = CreateContext("POST");
        Dictionary<string, StringValues> fields = [];
        context.Request.Form = new FormCollection(fields);
        RecordingStorage storage = new() { Content = "original" };

        await EditorEndpoint.Handler(context, storage);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage.Content, Is.EqualTo("original"));
        AssertRedirect(context, "Content is required.");
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task InvalidContentCountDoesNotOverwriteAsync(int count) {
        DefaultHttpContext context = CreateContext("POST");
        SetContent(context, new StringValues([.. Enumerable.Repeat("replacement", count)]));
        RecordingStorage storage = new() { Content = "original" };

        await EditorEndpoint.Handler(context, storage);

        Assert.That(storage.WriteCount, Is.Zero);
        Assert.That(storage.Content, Is.EqualTo("original"));
        AssertRedirect(context, "Content must have exactly one value.");
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public async Task StorageFailurePropagatesWithoutSuccessCookieAsync(string method) {
        DefaultHttpContext context = CreateContext(method);
        SetContent(context, new StringValues("replacement"));
        InvalidOperationException failure = new("Storage unavailable.");
        RecordingStorage storage = new() { Failure = failure };

        Assert.That(async () => await EditorEndpoint.Handler(context, storage), Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message));

        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
        Assert.That(context.Response.Headers.Location, Is.Empty);
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [TestCase("GET")]
    [TestCase("POST")]
    public void CancellationReachesStorageAndPropagatesAsync(string method) {
        DefaultHttpContext context = CreateContext(method);
        SetContent(context, new StringValues("replacement"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        RecordingStorage storage = new();

        Assert.That(async () => await EditorEndpoint.Handler(context, storage), Throws.InstanceOf<OperationCanceledException>());

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
        RecordingStorage storage = new() { Content = "original" };

        await EditorEndpoint.Handler(context, storage);

        Assert.That(storage.Content, Is.EqualTo(expectedContent));
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
        AssertRedirect(context, message);
    }

    private static DefaultHttpContext CreateContext(string method = "GET", string? path = "page.cs") {
        return TestHttp.CreateContext(path, method, "/edit/page.cs");
    }

    private static void SetContent(DefaultHttpContext context, StringValues content) {
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["content"] = content });
    }

    private static void AssertRedirect(DefaultHttpContext context, string message) {
        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(context.Request.Path.ToString()));
        Assert.That(context.Response.Headers.SetCookie.ToString(), Does.Contain("message=" + Uri.EscapeDataString(message)));
    }

    private sealed class RecordingStorage : IStorage {
        public string? Content { get; set; }
        public string? ReadPath { get; private set; }
        public string? WritePath { get; private set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public Exception? Failure { get; init; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken = default) {
            throw new NotSupportedException();
        }

        public Task<string?> GetFileContentsAsync(string path, CancellationToken cancellationToken = default) {
            CheckOperation(path, cancellationToken);
            ReadPath = path;
            ReadCount++;
            return Task.FromResult(Content);
        }

        public Task SetFileContentsAsync(string path, string content, CancellationToken cancellationToken = default) {
            CheckOperation(path, cancellationToken);
            WritePath = path;
            WriteCount++;
            Content = content;
            return Task.CompletedTask;
        }

        private void CheckOperation(string path, CancellationToken cancellationToken) {
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (Failure is not null) {
                throw Failure;
            }
        }
    }
}
