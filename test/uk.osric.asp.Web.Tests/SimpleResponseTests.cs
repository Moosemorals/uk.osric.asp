// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class SimpleResponseTests {

    [Test]
    public void WritingNullIsRefused() {
        SimpleResponse response = CreateResponse(out _);

        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() => response.Write(null!));
            Assert.Throws<ArgumentNullException>(() => response.WriteRaw(null!));
        });
    }

    [Test]
    public void WriteEscapesHtml() {
        SimpleResponse response = CreateResponse(out _);

        response.Write("<b>&");

        Assert.That(response.GetBody(), Is.EqualTo("&lt;b&gt;&amp;"));
    }

    [Test]
    public void WriteRawDoesNotEscapeHtml() {
        SimpleResponse response = CreateResponse(out _);

        response.WriteRaw("<b>&");

        Assert.That(response.GetBody(), Is.EqualTo("<b>&"));
    }

    // A layout is markup and nothing else: the content ran first and owns the response.
    [Test]
    public void ALayoutCannotSetTheResponse() {
        SimpleResponse response = CreateLayoutResponse(out RecordingLoggerFactory logs);

        response.SetStatus(201);
        response.SetContentType("text/plain; charset=utf-8");
        response.SetHeader("X-Page", "1");
        response.Redirect("/elsewhere");

        PageResponse built = response.ToResponse();

        Assert.Multiple(() => {
            Assert.That(built.Status, Is.EqualTo(200));
            Assert.That(built.ContentType, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(built.Headers.ContainsKey("X-Page"), Is.False);
            Assert.That(built.Location, Is.Null);
            Assert.That(logs.Entries.Count(entry => entry.Message.Contains("a layout cannot set the response")), Is.EqualTo(4));
        });
    }

    // The layout gate runs before validation, so a layout's bad argument is ignored rather than thrown.
    [Test]
    public void ALayoutsResponseSettingIsIgnoredBeforeItIsValidated() {
        SimpleResponse response = CreateLayoutResponse(out _);

        Assert.That(() => response.SetStatus(99), Throws.Nothing);
        Assert.That(response.ToResponse().Status, Is.EqualTo(200));
    }

    // The one setting a layout shares: its script is part of its chrome, so the chain unions it in.
    [Test]
    public void ALayoutMayAllowScripts() {
        SimpleResponse response = CreateLayoutResponse(out _);

        response.AllowScripts();

        Assert.That(response.ToResponse().AllowsScripts, Is.True);
    }

    [TestCase("GET", 302)]
    [TestCase("HEAD", 302)]
    [TestCase("POST", 303)]
    public void RedirectToLoginUsesTheStatusTheMethodNeeds(string method, int status) {
        SimpleResponse response = CreateResponse(out _, method: method);

        response.RedirectToLogin();

        Assert.That(response.ToResponse().Status, Is.EqualTo(status));
        Assert.That(response.ToResponse().Location, Is.EqualTo("/login"));
    }

    [Test]
    public void RedirectToLoginUsesTheTargetTheShellBuilt() {
        SimpleResponse response = CreateResponse(out _, loginRedirect: "/login?returnUrl=%2Fpage");

        response.RedirectToLogin();

        Assert.That(response.ToResponse().Location, Is.EqualTo("/login?returnUrl=%2Fpage"));
    }

    [Test]
    public void AntiforgeryFieldIsEmptyWithoutAFactory() {
        SimpleResponse response = CreateResponse(out _);

        Assert.That(response.AntiforgeryField, Is.Empty);
    }

    [Test]
    public void AntiforgeryFieldIsGeneratedOncePerRender() {
        int calls = 0;
        SimpleResponse response = CreateResponse(out _, antiforgeryField: () => {
            calls++;
            return "<input>";
        });

        Assert.That(response.AntiforgeryField, Is.EqualTo("<input>"));
        Assert.That(response.AntiforgeryField, Is.EqualTo("<input>"));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void AntiforgeryFieldIsEmptyAfterAbandonment() {
        int calls = 0;
        SimpleResponse response = CreateResponse(out _, antiforgeryField: () => {
            calls++;
            return "<input>";
        });

        response.Abandon();

        Assert.That(response.AntiforgeryField, Is.Empty);
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public void AnAbandonedRenderNeverMintsASecondToken() {
        int calls = 0;
        SimpleResponse response = CreateResponse(out _, antiforgeryField: () => {
            calls++;
            return "<input>";
        });

        _ = response.AntiforgeryField;
        response.Abandon();

        Assert.That(response.AntiforgeryField, Is.Empty);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void AResponseWithNoSinkCannotStream() {
        SimpleResponse response = CreateResponse(out _, canStream: false);

        Assert.That(() => response.FlushAsync(), Throws.InvalidOperationException.With.Message.Contains("cannot stream"));
    }

    [Test]
    public void ALayoutCannotStream() {
        SimpleResponse response = CreateResponse(out _, isLayout: true);

        Assert.That(() => response.FlushAsync(), Throws.InvalidOperationException.With.Message.Contains("layout"));
    }

    [Test]
    public void ARedirectCannotStream() {
        SimpleResponse response = CreateResponse(out _);

        response.Redirect("/elsewhere");

        Assert.That(() => response.FlushAsync(), Throws.InvalidOperationException.With.Message.Contains("redirect"));
    }

    [Test]
    public void AWrappedPageCannotStream() {
        SimpleResponse response = CreateResponse(out _, wouldWrap: _ => true);

        Assert.That(() => response.FlushAsync(),
            Throws.InvalidOperationException.With.Message.Contains("Set IPageLayout.Layout to null"));
        Assert.That(response.IsCommitted, Is.False);
    }

    [Test]
    public async Task ResponseSettingsAfterTheFlushThrow() {
        SimpleResponse response = CreateResponse(out _);
        await response.FlushAsync();

        Assert.Multiple(() => {
            Assert.That(() => response.SetStatus(201), Throws.InvalidOperationException);
            Assert.That(() => response.SetContentType("text/plain"), Throws.InvalidOperationException);
            Assert.That(() => response.SetHeader("X-Late", "1"), Throws.InvalidOperationException);
            Assert.That(() => response.AllowScripts(), Throws.InvalidOperationException);
            Assert.That(() => response.Redirect("/elsewhere"), Throws.InvalidOperationException);
            Assert.That(() => response.RedirectToLogin(), Throws.InvalidOperationException);
            Assert.That(() => _ = response.AntiforgeryField, Throws.InvalidOperationException);
        });
    }

    [Test]
    public async Task StreamingKeepsWriteEscapedAndWriteRawRaw() {
        SimpleResponse response = CreateResponse(out FakePageBodySink sink);

        response.Write("<b>");
        response.WriteRaw("<i>");
        await response.FlushAsync();

        Assert.That(sink.Chunks, Is.EqualTo(["&lt;b&gt;<i>"]));
    }

    private static SimpleResponse CreateResponse(
        out FakePageBodySink sink,
        string method = "GET",
        string loginRedirect = "/login",
        Func<string>? antiforgeryField = null,
        bool isLayout = false,
        bool canStream = true,
        Func<string, bool>? wouldWrap = null,
        RecordingLoggerFactory? loggerFactory = null) {
        sink = new FakePageBodySink();
        ILoggerFactory loggerFactoryOrNull = (ILoggerFactory?)loggerFactory ?? NullLoggerFactory.Instance;

        return new SimpleResponse(
            new PageResponseState("text/html; charset=utf-8"),
            new PageBody(
                new StringWriter(),
                PageHarness.Key("/page"),
                new BodyBudget(TestOptions.Render.MaxBodyChars),
                new PageCommitState(),
                canStream ? sink : null,
                CancellationToken.None),
            PageHarness.Key("/page"),
            TestHttp.Request("/page", method),
            loginRedirect,
            isLayout,
            loggerFactoryOrNull.CreateLogger(SimpleContext.PageCategory),
            antiforgeryField,
            wouldWrap);
    }

    private static SimpleResponse CreateLayoutResponse(out RecordingLoggerFactory logs) {
        logs = new RecordingLoggerFactory();

        return CreateResponse(out _, isLayout: true, loggerFactory: logs);
    }
}
