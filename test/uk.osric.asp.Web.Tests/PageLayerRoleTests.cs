// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;

/// <summary>What each of the three layer roles may ask of the response.</summary>
[TestFixture]
public sealed class PageLayerRoleTests {

    [TestCase(Content)]
    [TestCase(Layout)]
    [TestCase(Embedded)]
    public void EveryRoleMayAllowScripts(string role) {
        SimpleResponse response = Create(Role(role));

        response.AllowScripts();

        Assert.That(response.ToResponse().AllowsScripts, Is.True);
    }

    [TestCase(Content)]
    [TestCase(Layout)]
    [TestCase(Embedded)]
    public void EveryRoleCarriesTheRequestsAntiforgeryPair(string role) {
        SimpleResponse response = Create(Role(role), antiforgeryField: () => "<input>");

        Assert.That(response.AntiforgeryField, Is.EqualTo("<input>"));
    }

    [Test]
    public void TheContentOwnsTheResponse() {
        SimpleResponse response = Create(Role(Content));

        response.SetStatus(202);
        response.SetContentType("text/plain");
        response.SetHeader("X-Test", "1");
        response.Redirect("/elsewhere");

        PageResponse built = response.ToResponse();

        Assert.Multiple(() => {
            Assert.That(built.Status, Is.EqualTo(302));
            Assert.That(built.ContentType, Is.EqualTo("text/plain"));
            Assert.That(built.Headers["X-Test"], Is.EqualTo("1"));
            Assert.That(built.Location, Is.EqualTo("/elsewhere"));
        });
    }

    [TestCase(Layout)]
    [TestCase(Embedded)]
    public void ALayerThatDoesNotOwnTheResponseIgnoresItsSettings(string role) {
        SimpleResponse response = Create(Role(role), out RecordingLoggerFactory logs);

        response.SetStatus(202);
        response.SetContentType("text/plain");
        response.SetHeader("X-Test", "1");

        PageResponse built = response.ToResponse();

        Assert.Multiple(() => {
            Assert.That(built.Status, Is.EqualTo(200));
            Assert.That(built.ContentType, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(built.Headers.ContainsKey("X-Test"), Is.False);
            Assert.That(logs.Entries.Count(entry => entry.Message.Contains("cannot set the response", StringComparison.Ordinal)), Is.EqualTo(3));
        });
    }

    [Test]
    public void ALayoutThatRedirectsIsIgnored() {
        SimpleResponse response = Create(Role(Layout), out RecordingLoggerFactory logs);

        response.Redirect("/elsewhere");

        Assert.Multiple(() => {
            Assert.That(response.ToResponse().Location, Is.Null);
            Assert.That(logs.Entries.Any(entry => entry.Message.Contains("cannot set the response", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public void AnEmbeddedPageThatRedirectsIsRefused() {
        SimpleResponse response = Create(Role(Embedded));

        UserFacingException? refused = Assert.Throws<UserFacingException>(() => response.Redirect("/login"));

        Assert.Multiple(() => {
            Assert.That(refused?.Message, Does.Contain("/page"), "the embedder is named");
            Assert.That(refused?.Message, Does.Contain("/login"), "the target is named");
        });
    }

    [Test]
    public void OnlyTheContentMayStream() {
        Assert.That(async () => await Create(Role(Content)).FlushAsync(), Throws.Nothing);
    }

    [TestCase(Layout)]
    [TestCase(Embedded)]
    public void ALayerThatDoesNotOwnTheResponseMayNotStream(string role) {
        SimpleResponse response = Create(Role(role));

        InvalidOperationException? refused = Assert.Throws<InvalidOperationException>(
            () => response.FlushAsync().GetAwaiter().GetResult());

        Assert.That(refused?.Message, Does.Contain("/page"));
    }

    private const string Content = "content";
    private const string Layout = "layout";
    private const string Embedded = "embedded";

    private static PageLayerRole Role(string role) => role switch {
        Content => PageLayerRole.Content,
        Layout => PageLayerRole.Layout,
        _ => PageLayerRole.Embedded,
    };

    private static SimpleResponse Create(PageLayerRole role, Func<string>? antiforgeryField = null) =>
        Create(role, out _, antiforgeryField);

    private static SimpleResponse Create(
        PageLayerRole role,
        out RecordingLoggerFactory logs,
        Func<string>? antiforgeryField = null) {
        logs = new RecordingLoggerFactory();

        return new SimpleResponse(
            new PageResponseState("text/html; charset=utf-8"),
            new PageBody(
                new StringWriter(),
                PagePath.Normalize("/page"),
                new BodyBudget(TestOptions.Render.MaxBodyChars),
                new PageCommitState(),
                new FakePageBodySink(),
                CancellationToken.None),
            PagePath.Normalize("/page"),
            TestHttp.Request("/page"),
            "/login",
            role,
            logs.CreateLogger(SimpleContext.PageCategory),
            antiforgeryField,
            wouldWrap: null);
    }
}
