// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageSessionResolverTests {

    [Test]
    public async Task NoCookieMintsAnIdAndWritesNoRowAsync() {
        FakePageSessionStore store = new();
        DefaultHttpContext context = TestHttp.CreateContext("/page");

        PageSessionRequest session = await PageSessionResolver.ResolveAsync(
            context, store, TestSessions.Cookie, user: null);

        Assert.That(session.Presented, Is.False);
        Assert.That(session.Exists, Is.False);
        Assert.That(store.Ids, Is.Empty);
        Assert.That(session.Id, Has.Length.EqualTo(22));
    }

    [Test]
    public async Task TheSameRequestResolvesToTheSameSessionAsync() {
        FakePageSessionStore store = new();
        DefaultHttpContext context = TestHttp.CreateContext("/page");

        PageSessionRequest first = await PageSessionResolver.ResolveAsync(context, store, TestSessions.Cookie, user: null);
        PageSessionRequest second = await PageSessionResolver.ResolveAsync(context, store, TestSessions.Cookie, user: null);

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public async Task AKnownAnonymousSessionIsReusedAsync() {
        FakePageSessionStore store = new();
        string id = store.Create();
        DefaultHttpContext context = Present(id);

        PageSessionRequest session = await PageSessionResolver.ResolveAsync(context, store, TestSessions.Cookie, user: null);

        Assert.That(session.Id, Is.EqualTo(id));
        Assert.That(session.Presented, Is.True);
        Assert.That(session.Exists, Is.True);
        Assert.That(session.NeedsCookie, Is.False);
    }

    [Test]
    public async Task AnUnknownKeyStartsEmptyUnderTheSameIdAsync() {
        FakePageSessionStore store = new();
        string id = PageSessionCookie.Mint();
        DefaultHttpContext context = Present(id);

        PageSessionRequest session = await PageSessionResolver.ResolveAsync(context, store, TestSessions.Cookie, user: null);

        Assert.That(session.Id, Is.EqualTo(id), "the browser's id is as good as any other");
        Assert.That(session.Exists, Is.False);
    }

    [Test]
    public async Task ASessionOfTheSameAccountIsHonouredAsync() {
        FakePageSessionStore store = new();
        string id = store.Create("owner");
        DefaultHttpContext context = Present(id);

        PageSessionRequest session = await PageSessionResolver.ResolveAsync(
            context, store, TestSessions.Cookie, new PageUser("owner"));

        Assert.That(session.Id, Is.EqualTo(id));
        Assert.That(session.Exists, Is.True);
    }

    [Test]
    public async Task ASessionOfAnotherAccountIsDiscardedAsync() {
        FakePageSessionStore store = new();
        string id = store.Create("someone-else");
        DefaultHttpContext context = Present(id);

        PageSessionRequest session = await PageSessionResolver.ResolveAsync(
            context, store, TestSessions.Cookie, new PageUser("owner"));

        Assert.That(session.Id, Is.Not.EqualTo(id), "a stale cookie cannot inherit another account's session");
        Assert.That(session.Exists, Is.False);
        Assert.That(session.NeedsCookie, Is.False, "the old cookie is simply replaced on the next write");
        Assert.That(store.Ids, Does.Not.Contain(id), "the mismatched row is gone");
    }

    [Test]
    public async Task ASessionOfAnAccountIsDiscardedForAnAnonymousVisitorAsync() {
        FakePageSessionStore store = new();
        string id = store.Create("owner");
        DefaultHttpContext context = Present(id);

        PageSessionRequest session = await PageSessionResolver.ResolveAsync(context, store, TestSessions.Cookie, user: null);

        Assert.That(session.Id, Is.Not.EqualTo(id));
        Assert.That(store.Ids, Does.Not.Contain(id));
    }

    [TestCase("not-a-cookie")]
    [TestCase("")]
    [TestCase("dG9vLWxvbmctdG8tYmUtYS1zZXNzaW9u")]
    public async Task AMalformedCookieIsIgnoredAsync(string value) {
        FakePageSessionStore store = new();
        DefaultHttpContext context = TestHttp.CreateContext("/page");
        context.Request.Headers.Cookie = $"{TestSessions.Cookie.Name}={value}";

        PageSessionRequest session = await PageSessionResolver.ResolveAsync(context, store, TestSessions.Cookie, user: null);

        Assert.That(session.Presented, Is.False);
        Assert.That(session.Exists, Is.False);
    }

    private static DefaultHttpContext Present(string sessionId) {
        DefaultHttpContext context = TestHttp.CreateContext("/page");
        context.Request.Headers.Cookie = $"{TestSessions.Cookie.Name}={sessionId}";

        return context;
    }
}
