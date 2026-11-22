// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class SimpleContextTests {

    [TestCase("/app.js", "text/javascript; charset=utf-8")]
    [TestCase("/app.mjs", "text/javascript; charset=utf-8")]
    [TestCase("/app.css", "text/css; charset=utf-8")]
    [TestCase("/app.json", "application/json; charset=utf-8")]
    [TestCase("/app.txt", "text/plain; charset=utf-8")]
    [TestCase("/notes", "text/html; charset=utf-8")]
    public void TheKeyDecidesTheInitialContentType(string path, string contentType) {
        SimpleContext context = CreateContext("/page", out _, path);

        Assert.That(context.Response.ToResponse().ContentType, Is.EqualTo(contentType));
    }

    [Test]
    public void SetContentTypeOverridesAMappedType() {
        SimpleContext context = CreateContext("/page", out _, "/app.js");

        context.Response.SetContentType("text/html; charset=utf-8");

        Assert.That(context.Response.ToResponse().ContentType, Is.EqualTo("text/html; charset=utf-8"));
    }

    [Test]
    public void SetContentTypeOverridesTheFallback() {
        SimpleContext context = CreateContext("/page", out _, "/notes");

        context.Response.SetContentType("text/javascript; charset=utf-8");

        Assert.That(context.Response.ToResponse().ContentType, Is.EqualTo("text/javascript; charset=utf-8"));
    }

    [Test]
    public void AnUnmappedPageTakesTheConfiguredFallback() {
        PageContentTypes plain = PageContentTypes.Create("text/plain; charset=utf-8", []);
        SimpleContext context = CreateContext("/page", out _, "/notes", plain);

        Assert.That(context.Response.ToResponse().ContentType, Is.EqualTo("text/plain; charset=utf-8"));
    }

    [Test]
    public void UserIsNullByDefault() {
        SimpleContext context = CreateContext("/page", out _);

        Assert.That(context.User, Is.Null);
    }

    [Test]
    public void ASuppliedUserIsReturnedWithItsName() {
        SimpleContext context = CreateContext("/page", out _, user: new PageUser("owner"));

        Assert.That(context.User, Is.EqualTo(new PageUser("owner")));
        Assert.That(context.User?.Name, Is.EqualTo("owner"));
    }

    [Test]
    public void LogIsFiledUnderTheSharedPageCategory() {
        SimpleContext context = CreateContext("/about", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries, Has.Count.EqualTo(1));
        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages"));
        Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Information));
    }

    [Test]
    public void LogPutsThePagePathOnTheScope() {
        SimpleContext context = CreateContext("/about", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries[0].Scope["Path"], Is.EqualTo("/about"));
    }

    [Test]
    public void TheCategoryDoesNotVaryWithThePath() {
        SimpleContext first = CreateContext("/one", out RecordingLoggerFactory firstLogs);
        SimpleContext second = CreateContext("/two", out RecordingLoggerFactory secondLogs);

        first.Log("hello");
        second.Log("hello");

        Assert.That(firstLogs.Entries[0].Category, Is.EqualTo(secondLogs.Entries[0].Category));
    }

    [Test]
    public void LogMessageIsNotPrefixedWithThePath() {
        SimpleContext context = CreateContext("/about", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries[0].Message, Is.EqualTo("hello"));
    }

    [Test]
    public void RootPagePutsTheRootOnTheScope() {
        SimpleContext context = CreateContext("/", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries[0].Scope["Path"], Is.EqualTo("/"));
    }

    [Test]
    public void NestedPageKeepsItsSegmentsOnTheScope() {
        SimpleContext context = CreateContext("/a/b", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries[0].Scope["Path"], Is.EqualTo("/a/b"));
    }

    [Test]
    public void LogWarningIsFiledAtWarning() {
        SimpleContext context = CreateContext("/about", out RecordingLoggerFactory logs);

        context.LogWarning("careful");

        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages"));
        Assert.That(logs.Entries[0].Scope["Path"], Is.EqualTo("/about"));
        Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(logs.Entries[0].Message, Is.EqualTo("careful"));
    }

    [Test]
    public void LogErrorIsFiledAtError() {
        SimpleContext context = CreateContext("/about", out RecordingLoggerFactory logs);

        context.LogError("broken");

        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages"));
        Assert.That(logs.Entries[0].Scope["Path"], Is.EqualTo("/about"));
        Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Error));
        Assert.That(logs.Entries[0].Message, Is.EqualTo("broken"));
    }

    // A page-level store over an empty one, registered as the capability the endpoint offers.
    private static PageCapabilities CapabilitiesFor(PageStoresScope stores) {
#pragma warning disable CS0618 // The test proves the old capability still reaches the same store.
        return new PageCapabilities().Add<IPageStorage>(new PageStorageScope(stores.PageValues));
#pragma warning restore CS0618
    }

    private static SimpleContext CreateContext(
        string path,
        out RecordingLoggerFactory logs,
        string? requestPath = null,
        PageContentTypes? contentTypes = null,
        PageUser? user = null) {
        logs = new RecordingLoggerFactory();
        FakeStoreValues values = new();
        PageReaderSubject reader = new(
            new PageSessionRequest(uk.osric.asp.Web.Authentication.PageSessionCookie.Mint(), null, presented: false, exists: false),
            TestOptions.Stores.Visitor,
            browserId: null);
        PageStoresScope stores = new(
            PagePath.Normalize(path), reader, values, new FakeStoreMessages(), new ReservedPaths(ReservedPaths.Default),
            new FakePageSessionStore(values), TestOptions.Stores, new FakePageReadStore(), writable: true, commit: null);

        return new SimpleContext(
            logs,
            TestHttp.Request(requestPath ?? path),
            CancellationToken.None,
            new StringWriter(),
            CapabilitiesFor(stores),
            stores,
            contentTypes ?? TestContentTypes.Shipped,
            TestOptions.Render.MaxBodyChars,
            TestOptions.Editor.Key,
            user);
    }
}
