// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using uk.osric.asp.Core;
using uk.osric.asp.Web;

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
    public void LogIsFiledUnderThePageCategory() {
        SimpleContext context = CreateContext("/about", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries, Has.Count.EqualTo(1));
        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages./about"));
        Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Information));
    }

    [Test]
    public void LogMessageIsNotPrefixedWithThePath() {
        SimpleContext context = CreateContext("/about", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries[0].Message, Is.EqualTo("hello"));
    }

    [Test]
    public void RootPageLogsUnderTheRootCategory() {
        SimpleContext context = CreateContext("/", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages./"));
    }

    [Test]
    public void NestedPageKeepsItsSegmentsInTheCategory() {
        SimpleContext context = CreateContext("/a/b", out RecordingLoggerFactory logs);

        context.Log("hello");

        Assert.That(logs.Entries[0].Category, Is.EqualTo("uk.osric.asp.Pages./a/b"));
    }

    [Test]
    public void RequestValuesAreExposedToThePage() {
        SimpleContext context = CreateContext(out _);

        Assert.That(context.Request.Method, Is.EqualTo("GET"));
        Assert.That(context.Path, Is.EqualTo("/page"));
        Assert.That(context.Aborted, Is.EqualTo(CancellationToken.None));
    }

    private static SimpleContext CreateContext(out StringWriter output) {
        output = new StringWriter();
        return new SimpleContext(NullLoggerFactory.Instance, TestHttp.Request("/page"), CancellationToken.None, output);
    }

    private static SimpleContext CreateContext(string path, out RecordingLoggerFactory logs) {
        logs = new RecordingLoggerFactory();
        return new SimpleContext(logs, TestHttp.Request(path), CancellationToken.None, new StringWriter());
    }
}
