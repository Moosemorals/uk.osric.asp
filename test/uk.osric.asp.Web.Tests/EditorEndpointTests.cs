// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using System.Net;
using System.Text;
using uk.osric.asp.Core;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Endpoints;

[TestFixture]
public sealed class EditorEndpointTests {

    private static readonly IWebHostEnvironment Production = new TestEnvironment("Production");
    [TestCase("plain text")]
    [TestCase("&amp; <tag> \"quoted\"")]
    [TestCase("</textarea><script>alert('source')</script>")]
    [TestCase("line one\nline two: \u00e9")]
    public async Task GetEncodesStoredSourceAsync(string source) {
        DefaultHttpContext context = CreateContext();
        RecordingStorage storage = new() { Content = source };

        await EditorEndpoint.Handler(context, storage, Production);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(body, Does.Contain($"<textarea name=\"content\" rows=\"10\" cols=\"50\">{WebUtility.HtmlEncode(source)}</textarea>"));
        Assert.That(storage.ReadPath?.Path, Is.EqualTo("/page.cs"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task MissingPageShowsEmptyEditorAsync() {
        DefaultHttpContext context = CreateContext();
        RecordingStorage storage = new();

        await EditorEndpoint.Handler(context, storage, Production);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(await TestHttp.ReadBodyAsync(context), Does.Contain("cols=\"50\"></textarea>"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task GetWithoutUsablePathReadsTheRootPageAsync(string? path) {
        DefaultHttpContext context = CreateContext(path: path);
        RecordingStorage storage = new();

        await EditorEndpoint.Handler(context, storage, Production);

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(storage.ReadPath?.Path, Is.EqualTo("/"));
    }

    [Test]
    public async Task GetEncodesPathAndConsumesEncodedMessageAsync() {
        DefaultHttpContext context = CreateContext(path: "<page>&");
        context.Request.Headers.Cookie = "message=" + Uri.EscapeDataString("<script>saved</script>");

        await EditorEndpoint.Handler(context, new RecordingStorage(), Production);

        string body = await TestHttp.ReadBodyAsync(context);
        Assert.That(body, Does.Contain("Editing /&lt;page&gt;&amp;"));
        Assert.That(body, Does.Contain("<p>&lt;script&gt;saved&lt;/script&gt;</p>"));
        Assert.That(context.Response.Headers.SetCookie.ToString(), Does.Contain("message=;"));
        Assert.That(context.Response.Headers.SetCookie.ToString(), Does.Contain("expires="));
    }

    [Test]
    public async Task GetWithoutMessageDoesNotSetCookieAsync() {
        DefaultHttpContext context = CreateContext();

        await EditorEndpoint.Handler(context, new RecordingStorage(), Production);

        Assert.That(context.Response.Headers.SetCookie, Is.Empty);
    }

    [TestCase("")]
    [TestCase("hello & <world>\n\u00e9")]
    public async Task PostSavesExactContentAndRedirectsAsync(string source) {
        DefaultHttpContext context = CreateContext("POST");
        SetContent(context, new StringValues(source));
        RecordingStorage storage = new();

        await EditorEndpoint.Handler(context, storage, Production);

        Assert.That(storage.WriteCount, Is.EqualTo(1));
        Assert.That(storage.WritePath?.Path, Is.EqualTo("/page.cs"));
        Assert.That(storage.Content, Is.EqualTo(source));
        Assert.That(storage.ReadCount, Is.Zero);
        AssertRedirect(context, "Saved successfully.");
        Assert.That(await TestHttp.ReadBodyAsync(context), Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/")]
    public async Task PostWithoutUsablePathWritesTheRootPageAsync(string? path) {
        DefaultHttpContext context = CreateContext("POST", path);
        SetContent(context, new StringValues("replacement"));
        RecordingStorage storage = new() { Content = "original" };

        await EditorEndpoint.Handler(context, storage, Production);

        Assert.That(storage.WritePath?.Path, Is.EqualTo("/"));
        Assert.That(storage.Content, Is.EqualTo("replacement"));
        AssertRedirect(context, "Saved successfully.");
    }

    [TestCase("/edit/Hello", "/edit/hello")]
    [TestCase("/edit/hello/", "/edit/hello")]
    public async Task NonCanonicalEditorUrlRedirectsPermanentlyAsync(string requestPath, string expectedLocation) {
        DefaultHttpContext context = TestHttp.CreateContext(requestPath["/edit/".Length..], "GET", requestPath);
        RecordingStorage storage = new();

        await EditorEndpoint.Handler(context, storage, Production);

        Assert.That(context.Response.StatusCode, Is.EqualTo(308));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
        Assert.That(storage.ReadCount, Is.Zero);
    }

    [Test]
    public async Task PostFromNonCanonicalUrlSavesTheCanonicalKeyAsync() {
        DefaultHttpContext context = TestHttp.CreateContext("Hello/", "POST", "/edit/Hello/");
        SetContent(context, new StringValues("source"));
        RecordingStorage storage = new();

        await EditorEndpoint.Handler(context, storage, Production);

        Assert.That(storage.WritePath?.Path, Is.EqualTo("/hello"));
        Assert.That(storage.Content, Is.EqualTo("source"));
        Assert.That(context.Response.StatusCode, Is.EqualTo(302));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/edit/hello"));
    }

    [Test]
    public async Task MissingContentDoesNotOverwriteAsync() {
        DefaultHttpContext context = CreateContext("POST");
        Dictionary<string, StringValues> fields = [];
        context.Request.Form = new FormCollection(fields);
        RecordingStorage storage = new() { Content = "original" };

        await EditorEndpoint.Handler(context, storage, Production);

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

        await EditorEndpoint.Handler(context, storage, Production);

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

        Assert.That(async () => await EditorEndpoint.Handler(context, storage, Production), Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message));

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

        Assert.That(async () => await EditorEndpoint.Handler(context, storage, Production), Throws.InstanceOf<OperationCanceledException>());

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

        await EditorEndpoint.Handler(context, storage, Production);

        Assert.That(storage.Content, Is.EqualTo(expectedContent));
        Assert.That(storage.WriteCount, Is.EqualTo(writes));
        AssertRedirect(context, message);
    }

    [TestCase("Production", true)]
    [TestCase("Development", false)]
    public async Task FlashCookieIsSecureOnlyInProductionAsync(string environment, bool secure) {
        DefaultHttpContext context = CreateContext("POST");
        SetContent(context, new StringValues("source"));

        await EditorEndpoint.Handler(context, new RecordingStorage(), new TestEnvironment(environment));

        string cookie = context.Response.Headers.SetCookie.ToString();
        Assert.That(cookie, Does.Contain("message="));
        Assert.That(cookie, Does.Contain("httponly"));
        Assert.That(cookie, Does.Contain("samesite=strict"));
        Assert.That(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase), Is.EqualTo(secure));
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

    private sealed class RecordingStorage : IStorage {
        public string? Content { get; set; }
        public PagePath? ReadPath { get; private set; }
        public PagePath? WritePath { get; private set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public Exception? Failure { get; init; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<string?> GetFileContentsAsync(PagePath path, CancellationToken cancellationToken = default) {
            CheckOperation(cancellationToken);
            ReadPath = path;
            ReadCount++;
            return Task.FromResult(Content);
        }

        public Task SetFileContentsAsync(PagePath path, string content, CancellationToken cancellationToken = default) {
            CheckOperation(cancellationToken);
            WritePath = path;
            WriteCount++;
            Content = content;
            return Task.CompletedTask;
        }

        private void CheckOperation(CancellationToken cancellationToken) {
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) {
                throw Failure;
            }
        }
    }
}
