// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class SimpleContextTests {

    [Test]
    public void ResponseDefaultsToOkHtml() {
        SimpleContext context = CreateContext(out _);

        PageResponse response = context.ToResponse();

        Assert.That(response.Status, Is.EqualTo(200));
        Assert.That(response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(response.Headers, Is.Empty);
        Assert.That(response.Location, Is.Null);
        Assert.That(response.AllowsScripts, Is.False);
    }

    [Test]
    public void AllowScriptsIsRecordedInTheResponse() {
        SimpleContext context = CreateContext(out _);

        context.AllowScripts();

        Assert.That(context.ToResponse().AllowsScripts, Is.True);
    }

    [Test]
    public void AllowScriptsIsIdempotent() {
        SimpleContext context = CreateContext(out _);

        context.AllowScripts();
        context.AllowScripts();

        Assert.That(context.ToResponse().AllowsScripts, Is.True);
    }

    [TestCase("Cache-Control")]
    [TestCase("cache-control")]
    [TestCase("Content-Language")]
    [TestCase("ETag")]
    [TestCase("Expires")]
    [TestCase("Vary")]
    [TestCase("X-Robots-Tag")]
    public void AllowedHeadersAreRecorded(string name) {
        SimpleContext context = CreateContext(out _);

        context.SetHeader(name, "value");

        Assert.That(context.ToResponse().Headers[name], Is.EqualTo("value"));
    }

    [TestCase("Set-Cookie")]
    [TestCase("Content-Length")]
    [TestCase("Content-Type")]
    [TestCase("Location")]
    [TestCase("Transfer-Encoding")]
    [TestCase("Connection")]
    public void ReservedHeadersAreRefused(string name) {
        SimpleContext context = CreateContext(out _);

        Assert.That(() => context.SetHeader(name, "value"), Throws.ArgumentException);
        Assert.That(context.ToResponse().Headers, Is.Empty);
    }

    [TestCase("X-Content-Type-Options")]
    [TestCase("x-content-type-options")]
    [TestCase("X-Frame-Options")]
    [TestCase("X-XSS-Protection")]
    public void TheShellsOwnXHeadersAreRefusedThroughTheWildcard(string name) {
        SimpleContext context = CreateContext(out _);

        Assert.That(() => context.SetHeader(name, ""), Throws.ArgumentException);
        Assert.That(() => context.SetHeader(name, "value"), Throws.ArgumentException);
        Assert.That(context.ToResponse().Headers, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void EmptyHeaderValuesAreRefused(string value) {
        SimpleContext context = CreateContext(out _);

        Assert.That(() => context.SetHeader("X-Page", value), Throws.ArgumentException);
        Assert.That(context.ToResponse().Headers, Is.Empty);
    }

    [TestCase("value\r\nSet-Cookie: stolen=yes")]
    [TestCase("value\n")]
    public void HeaderValuesWithLineBreaksAreRefused(string value) {
        SimpleContext context = CreateContext(out _);

        Assert.That(() => context.SetHeader("X-Page", value), Throws.ArgumentException);
    }

    [TestCase(99)]
    [TestCase(600)]
    public void StatusOutsideTheRangeIsRefused(int status) {
        SimpleContext context = CreateContext(out _);

        Assert.That(() => context.SetStatus(status), Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(context.ToResponse().Status, Is.EqualTo(200));
    }

    [Test]
    public void RedirectRecordsTheLocationAndTheStatus() {
        SimpleContext context = CreateContext(out _);

        context.Redirect("/elsewhere");

        Assert.That(context.ToResponse().Location, Is.EqualTo("/elsewhere"));
        Assert.That(context.ToResponse().Status, Is.EqualTo(302));
    }

    [Test]
    public void RedirectKeepsAnExplicitStatus() {
        SimpleContext context = CreateContext(out _);

        context.Redirect("/moved", 301);

        Assert.That(context.ToResponse().Status, Is.EqualTo(301));
    }

    [TestCase("value\r\nSet-Cookie: stolen=yes")]
    [TestCase("value\n")]
    public void RedirectTargetsWithLineBreaksAreRefused(string location) {
        SimpleContext context = CreateContext(out _);

        Assert.That(() => context.Redirect(location), Throws.ArgumentException);
        Assert.That(context.ToResponse().Location, Is.Null);
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

    // One metrics instance per context would leak meters, so the context tests share one and never
    // assert on it.
    private static readonly PageMetrics s_metrics = new();

    // A page-level store over an empty one, which is what the endpoint builds for a page.
    private static PageStorageScope StorageFor(string path) {
        return new PageStorageScope(
            PagePath.Normalize(path), new FakePageStateStore(), new PageStorageOptions(), writable: true);
    }

    private static SimpleContext CreateContext(
        out StringWriter output,
        PageUser? user = null,
        string loginRedirect = "/login",
        Func<string>? antiforgeryField = null,
        string method = "GET") {
        output = new StringWriter();
        return new SimpleContext(
            NullLoggerFactory.Instance,
            TestHttp.Request("/page", method),
            CancellationToken.None,
            output,
            StorageFor("/page"),
            new FakePageFetcher(),
            new PageFetchOptions(),
            s_metrics,
            RenderOptions.DefaultMaxBodyChars,
            user,
            loginRedirect,
            antiforgeryField);
    }

    private static SimpleContext CreateContext(string path, out RecordingLoggerFactory logs) {
        logs = new RecordingLoggerFactory();
        return new SimpleContext(
            logs,
            TestHttp.Request(path),
            CancellationToken.None,
            new StringWriter(),
            StorageFor("/page"),
            new FakePageFetcher(),
            new PageFetchOptions(),
            s_metrics);
    }
}
