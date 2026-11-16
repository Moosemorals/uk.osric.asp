// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class SimpleContextTests {

    [Test]
    public void WritingNullIsRefused() {
        SimpleContext context = CreateContext(out _);

        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() => context.Write(null!));
            Assert.Throws<ArgumentNullException>(() => context.WriteRaw(null!));
        });
    }

    [TestCase("/app.js", "text/javascript; charset=utf-8")]
    [TestCase("/app.mjs", "text/javascript; charset=utf-8")]
    [TestCase("/app.css", "text/css; charset=utf-8")]
    [TestCase("/app.json", "application/json; charset=utf-8")]
    [TestCase("/app.txt", "text/plain; charset=utf-8")]
    [TestCase("/notes", "text/html; charset=utf-8")]
    public void TheKeyDecidesTheInitialContentType(string path, string contentType) {
        SimpleContext context = CreateContext(out _, path: path);

        Assert.That(context.ToResponse().ContentType, Is.EqualTo(contentType));
    }

    [Test]
    public void SetContentTypeOverridesAMappedType() {
        SimpleContext context = CreateContext(out _, path: "/app.js");

        context.SetContentType("text/html; charset=utf-8");

        Assert.That(context.ToResponse().ContentType, Is.EqualTo("text/html; charset=utf-8"));
    }

    [Test]
    public void SetContentTypeOverridesTheFallback() {
        SimpleContext context = CreateContext(out _, path: "/notes");

        context.SetContentType("text/javascript; charset=utf-8");

        Assert.That(context.ToResponse().ContentType, Is.EqualTo("text/javascript; charset=utf-8"));
    }

    [Test]
    public void AnUnmappedPageTakesTheConfiguredFallback() {
        PageContentTypes plain = PageContentTypes.Create("text/plain; charset=utf-8", []);
        SimpleContext context = CreateContext(out _, path: "/notes", contentTypes: plain);

        Assert.That(context.ToResponse().ContentType, Is.EqualTo("text/plain; charset=utf-8"));
    }

    // A layout is markup and nothing else: the content ran first and owns the response.
    [Test]
    public void ALayoutCannotSetTheResponse() {
        SimpleContext context = CreateLayoutContext(out RecordingLoggerFactory logs);

        context.SetStatus(201);
        context.SetContentType("text/plain; charset=utf-8");
        context.SetHeader("X-Page", "1");
        context.Redirect("/elsewhere");

        PageResponse response = context.ToResponse();

        Assert.Multiple(() => {
            Assert.That(response.Status, Is.EqualTo(200));
            Assert.That(response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(response.Headers.ContainsKey("X-Page"), Is.False);
            Assert.That(response.Location, Is.Null);
            Assert.That(logs.Entries.Count(entry => entry.Message.Contains("a layout cannot set the response")), Is.EqualTo(4));
        });
    }

    // The layout gate runs before validation, so a layout's bad argument is ignored rather than thrown.
    [Test]
    public void ALayoutsResponseSettingIsIgnoredBeforeItIsValidated() {
        SimpleContext context = CreateLayoutContext(out _);

        Assert.That(() => context.SetStatus(99), Throws.Nothing);
        Assert.That(context.ToResponse().Status, Is.EqualTo(200));
    }

    // The one setting a layout shares: its script is part of its chrome, so the chain unions it in.
    [Test]
    public void ALayoutMayAllowScripts() {
        SimpleContext context = CreateLayoutContext(out _);

        context.AllowScripts();

        Assert.That(context.ToResponse().AllowsScripts, Is.True);
    }

    [Test]
    public void UserIsNullByDefault() {
        SimpleContext context = CreateContext(out _);

        Assert.That(context.User, Is.Null);
    }

    [Test]
    public void ASuppliedUserIsReturnedWithItsName() {
        SimpleContext context = CreateContext(out _, user: new PageUser("owner"));

        Assert.That(context.User, Is.EqualTo(new PageUser("owner")));
        Assert.That(context.User?.Name, Is.EqualTo("owner"));
    }

    [TestCase("GET", 302)]
    [TestCase("HEAD", 302)]
    [TestCase("POST", 303)]
    public void RedirectToLoginUsesTheStatusTheMethodNeeds(string method, int status) {
        SimpleContext context = CreateContext(out _, method: method);

        context.RedirectToLogin();

        Assert.That(context.ToResponse().Status, Is.EqualTo(status));
        Assert.That(context.ToResponse().Location, Is.EqualTo("/login"));
    }

    [Test]
    public void RedirectToLoginUsesTheTargetTheShellBuilt() {
        SimpleContext context = CreateContext(out _, loginRedirect: "/login?returnUrl=%2Fpage");

        context.RedirectToLogin();

        Assert.That(context.ToResponse().Location, Is.EqualTo("/login?returnUrl=%2Fpage"));
    }

    [Test]
    public void AntiforgeryFieldIsEmptyWithoutAFactory() {
        SimpleContext context = CreateContext(out _);

        Assert.That(context.AntiforgeryField, Is.Empty);
    }

    [Test]
    public void AntiforgeryFieldIsGeneratedOncePerRender() {
        int calls = 0;
        SimpleContext context = CreateContext(out _, antiforgeryField: () => {
            calls++;
            return "<input>";
        });

        Assert.That(context.AntiforgeryField, Is.EqualTo("<input>"));
        Assert.That(context.AntiforgeryField, Is.EqualTo("<input>"));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void AntiforgeryFieldIsEmptyAfterAbandonment() {
        int calls = 0;
        SimpleContext context = CreateContext(out _, antiforgeryField: () => {
            calls++;
            return "<input>";
        });

        context.Abandon();

        Assert.That(context.AntiforgeryField, Is.Empty);
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public void AnAbandonedRenderNeverMintsASecondToken() {
        int calls = 0;
        SimpleContext context = CreateContext(out _, antiforgeryField: () => {
            calls++;
            return "<input>";
        });

        _ = context.AntiforgeryField;
        context.Abandon();

        Assert.That(context.AntiforgeryField, Is.Empty);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void WriteEscapesHtml() {
        SimpleContext context = CreateContext(out StringWriter output);

        context.Write("<b>&");

        Assert.That(output.ToString(), Is.EqualTo("&lt;b&gt;&amp;"));
    }

    [Test]
    public void WriteRawDoesNotEscapeHtml() {
        SimpleContext context = CreateContext(out StringWriter output);

        context.WriteRaw("<b>&");

        Assert.That(output.ToString(), Is.EqualTo("<b>&"));
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

    [Test]
    public void AContextWithNoSinkCannotStream() {
        SimpleContext context = CreateContext(out _);

        Assert.That(() => context.FlushAsync(), Throws.InvalidOperationException.With.Message.Contains("cannot stream"));
    }

    [Test]
    public void ALayoutCannotStream() {
        SimpleContext context = CreateStreamingContext(out _, out _, isLayout: true);

        Assert.That(() => context.FlushAsync(), Throws.InvalidOperationException.With.Message.Contains("layout"));
    }

    [Test]
    public void ARedirectCannotStream() {
        SimpleContext context = CreateStreamingContext(out _, out _);

        context.Redirect("/elsewhere");

        Assert.That(() => context.FlushAsync(), Throws.InvalidOperationException.With.Message.Contains("redirect"));
    }

    [Test]
    public void AWrappedPageCannotStream() {
        SimpleContext context = CreateStreamingContext(out _, out _, wouldWrap: _ => true);

        Assert.That(() => context.FlushAsync(),
            Throws.InvalidOperationException.With.Message.Contains("Set IPageLayout.Layout to null"));
        Assert.That(context.IsCommitted, Is.False);
    }

    [Test]
    public async Task ResponseSettingsAfterTheFlushThrow() {
        SimpleContext context = CreateStreamingContext(out _, out _);
        await context.FlushAsync();

        Assert.Multiple(() => {
            Assert.That(() => context.SetStatus(201), Throws.InvalidOperationException);
            Assert.That(() => context.SetContentType("text/plain"), Throws.InvalidOperationException);
            Assert.That(() => context.SetHeader("X-Late", "1"), Throws.InvalidOperationException);
            Assert.That(() => context.AllowScripts(), Throws.InvalidOperationException);
            Assert.That(() => context.Redirect("/elsewhere"), Throws.InvalidOperationException);
            Assert.That(() => context.RedirectToLogin(), Throws.InvalidOperationException);
            Assert.That(() => _ = context.AntiforgeryField, Throws.InvalidOperationException);
        });
    }

    [Test]
    public async Task StreamingKeepsWriteEscapedAndWriteRawRaw() {
        SimpleContext context = CreateStreamingContext(out _, out FakePageBodySink sink);

        context.Write("<b>");
        context.WriteRaw("<i>");
        await context.FlushAsync();

        Assert.That(sink.Chunks, Is.EqualTo(["&lt;b&gt;<i>"]));
    }

    // A page-level store over an empty one, registered as the capability the endpoint offers.
    private static PageCapabilities CapabilitiesFor(string path) {
        PageStorageScope storage = new(
            PagePath.Normalize(path), new FakePageStateStore(), TestOptions.Storage, writable: true);

        return new PageCapabilities().Add<IPageStorage>(storage);
    }

    private static SimpleContext CreateContext(
        out StringWriter output,
        PageUser? user = null,
        string loginRedirect = "/login",
        Func<string>? antiforgeryField = null,
        string method = "GET",
        string path = "/page",
        PageContentTypes? contentTypes = null) {
        output = new StringWriter();
        return new SimpleContext(
            NullLoggerFactory.Instance,
            TestHttp.Request(path, method),
            CancellationToken.None,
            output,
            CapabilitiesFor(path),
            contentTypes ?? TestContentTypes.Shipped,
            TestOptions.Render.MaxBodyChars,
            TestOptions.Editor.Key,
            user,
            loginRedirect,
            antiforgeryField);
    }

    private static SimpleContext CreateStreamingContext(
        out StringWriter output,
        out FakePageBodySink sink,
        bool isLayout = false,
        Func<string, bool>? wouldWrap = null,
        int maxBodyChars = 4 * 1024 * 1024) {
        output = new StringWriter();
        sink = new FakePageBodySink();

        return new SimpleContext(
            NullLoggerFactory.Instance,
            TestHttp.Request("/page"),
            CancellationToken.None,
            output,
            CapabilitiesFor("/page"),
            TestContentTypes.Shipped,
            maxBodyChars,
            TestOptions.Editor.Key,
            isLayout: isLayout,
            bodySink: sink,
            wouldWrap: wouldWrap);
    }

    private static SimpleContext CreateLayoutContext(out RecordingLoggerFactory logs) {
        logs = new RecordingLoggerFactory();
        return new SimpleContext(
            logs,
            TestHttp.Request("/page"),
            CancellationToken.None,
            new StringWriter(),
            CapabilitiesFor("/page"),
            TestContentTypes.Shipped,
            TestOptions.Render.MaxBodyChars,
            TestOptions.Editor.Key,
            isLayout: true);
    }

    private static SimpleContext CreateContext(string path, out RecordingLoggerFactory logs) {
        logs = new RecordingLoggerFactory();
        return new SimpleContext(
            logs,
            TestHttp.Request(path),
            CancellationToken.None,
            new StringWriter(),
            CapabilitiesFor("/page"),
            TestContentTypes.Shipped,
            TestOptions.Render.MaxBodyChars,
            TestOptions.Editor.Key);
    }
}
