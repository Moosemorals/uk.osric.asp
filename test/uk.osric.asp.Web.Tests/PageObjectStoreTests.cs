// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

/// <summary>The handle a page reaches objects through: the lifetimes, the content types and the verbs.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageObjectStoreTests {

    private const string PageKey = "/page";
    private const string OtherKey = "/other";

    private readonly string _sessionId = PageSessionCookie.Mint();
    private readonly FakeStoreObjects _objects = new();
    private readonly FakePageSessionStore _sessions = new();

    [Test]
    public async Task APagePutsAndOpensItsOwnObjectAsync() {
        PageObjectStore store = Store(PageKey);
        byte[] content = Encoding.UTF8.GetBytes("an image, honestly");

        PageObjectRef reference = await store.PutAsync(content, "image/png");
        PageObjectStream? opened = await store.OpenAsync(reference.Sha);

        Assert.That(opened, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(reference.Length, Is.EqualTo(content.Length));
            Assert.That(reference.ContentType, Is.EqualTo("image/png"));
            Assert.That(opened!.ContentType, Is.EqualTo("image/png"));
            Assert.That(opened.Length, Is.EqualTo(content.Length));
        });
    }

    [Test]
    public async Task APageObjectIsVisibleToAnotherPageAsync() {
        PageObjectRef reference = await Store(PageKey).PutAsync(Encoding.UTF8.GetBytes("shared"), "text/plain; charset=utf-8");

        PageObjectStream? opened = await Store(OtherKey).OpenAsync(reference.Sha);

        Assert.That(opened, Is.Not.Null);
    }

    [Test]
    public async Task AVisitorObjectIsOnlyTheReadersAsync() {
        PageObjectStore owner = VisitorStore(Reader(sessionId: "owner"));
        PageObjectRef reference = await owner.PutAsync(Encoding.UTF8.GetBytes("mine"), "application/octet-stream");

        await Assert.MultipleAsync(async () => {
            Assert.That(await VisitorStore(Reader(sessionId: "stranger")).OpenAsync(reference.Sha), Is.Null);
            Assert.That(await VisitorStore(Reader(sessionId: "owner")).OpenAsync(reference.Sha), Is.Not.Null);
        });
    }

    [Test]
    public async Task ASignedInObjectIsOnTheAccountAsync() {
        PageObjectRef reference = await VisitorStore(Reader(account: "owner"))
            .PutAsync(Encoding.UTF8.GetBytes("mine"), "application/octet-stream");

        await Assert.MultipleAsync(async () => {
            Assert.That(await VisitorStore(Reader(account: "stranger")).ExistsAsync(reference.Sha), Is.False);
            Assert.That(await VisitorStore(Reader(account: "owner")).ExistsAsync(reference.Sha), Is.True);
        });
    }

    [Test]
    public void ALifetimeTheSubjectCannotCarryIsAPageBug() {
        PageObjectStore page = Store(PageKey);
        PageObjectStore visitor = VisitorStore(Reader(sessionId: "one"));

        Assert.Multiple(() => {
            _ = Assert.ThrowsAsync<ArgumentException>(
                () => page.PutAsync([1], "application/octet-stream", StoreLifetime.Person));
            _ = Assert.ThrowsAsync<ArgumentException>(
                () => page.PutAsync([1], "application/octet-stream", StoreLifetime.NextRead));
            _ = Assert.ThrowsAsync<ArgumentException>(
                () => visitor.PutAsync([1], "application/octet-stream", StoreLifetime.Page));
            _ = Assert.ThrowsAsync<ArgumentException>(
                () => visitor.PutAsync([1], "application/octet-stream", StoreLifetime.NextRead));
        });
    }

    [Test]
    public async Task AHeadMayOpenAndMayNotPutAsync() {
        PageObjectRef reference = await Store(PageKey).PutAsync(Encoding.UTF8.GetBytes("bytes"), "application/octet-stream");
        PageObjectStore head = Store(PageKey, writable: false);

        Assert.That(await head.OpenAsync(reference.Sha), Is.Not.Null);
        _ = Assert.ThrowsAsync<InvalidOperationException>(
            () => head.PutAsync(Encoding.UTF8.GetBytes("more"), "application/octet-stream"));
    }

    [Test]
    public async Task ABrowserObjectMintsTheBrowserCookieAsync() {
        PageReaderSubject reader = Reader(browserId: null);
        PageObjectStore store = VisitorStore(reader);

        _ = await store.PutAsync(Encoding.UTF8.GetBytes("device"), "application/octet-stream", StoreLifetime.Browser);

        Assert.Multiple(() => {
            Assert.That(reader.BrowserId, Is.Not.Null);
            Assert.That(reader.NeedsBrowserCookie, Is.True);
        });
    }

    [Test]
    public async Task ARefusedContentTypeNeverReachesTheStoreAsync() {
        PageObjectStore store = Store(PageKey);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => store.PutAsync(Encoding.UTF8.GetBytes("<svg/>"), "image/svg+xml"));

        Assert.That(_objects.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AnObjectOverItsLimitNeverReachesTheStoreAsync() {
        PageObjectStore store = Store(PageKey, limits: TestOptions.Stores.Objects with { MaxObjectBytes = 3 });

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => store.PutAsync(Encoding.UTF8.GetBytes("four"), "application/octet-stream"));

        Assert.That(_objects.WriteCount, Is.Zero);
    }

    [Test]
    public async Task AMalformedDigestIsAPageBugAsync() {
        PageObjectStore store = Store(PageKey);

        await Assert.MultipleAsync(async () => {
            _ = await Assert.ThrowsAsync<ArgumentException>(() => store.OpenAsync("not a digest"));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => store.ExistsAsync("not a digest"));
        });
    }

    [Test]
    public async Task DisposeClosesEveryReaderTheHandleIssuedAsync() {
        PageObjectRef reference = await Store(PageKey).PutAsync(Encoding.UTF8.GetBytes("bytes"), "application/octet-stream");
        PageObjectStore store = Store(PageKey);
        PageObjectStream? opened = await store.OpenAsync(reference.Sha);

        await store.DisposeAsync();

        Assert.That(((FakeObjectStream)opened!).Closed, Is.True);
    }

    private PageReaderSubject Reader(string? account = null, string? sessionId = null, string? browserId = null) =>
        new(
            new PageSessionRequest(
                sessionId ?? _sessionId,
                account,
                presented: sessionId is not null,
                exists: sessionId is not null),
            TestOptions.Stores.Visitor,
            browserId);

    private PageObjectStore Store(string key, bool writable = true, PageObjectOptions? limits = null) =>
        new(
            PagePath.Normalize(key),
            _objects,
            limits ?? TestOptions.Stores.Objects,
            writable);

    private PageObjectStore VisitorStore(PageReaderSubject reader, bool writable = true, PageObjectOptions? limits = null) =>
        new(
            PagePath.Normalize(PageKey),
            _objects,
            _sessions,
            reader,
            limits ?? TestOptions.Stores.Objects,
            writable,
            commit: null);
}
