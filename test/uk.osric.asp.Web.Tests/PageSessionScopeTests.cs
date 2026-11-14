// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageSessionScopeTests {

    [Test]
    public async Task AValueOneVisitorWritesIsInvisibleToAnotherAsync() {
        FakePageSessionStore store = new();
        IPageSession first = Scope(store, "/page");
        IPageSession second = Scope(store, "/page");

        await first.SetAsync("draft", "one");

        Assert.That((await second.TryGetValueAsync<string>("draft")).Found, Is.False);
        Assert.That(await second.KeysAsync(), Is.Empty);
    }

    [Test]
    public async Task APageWritesUnderItsOwnPathAndReadsItBackAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        IPageSession page = new PageSessionScope(Key("/page"), store, TestOptions.Session, session, writable: true, slides: true);

        await page.SetAsync("draft", "hello");

        Assert.That(store.Raw(session.Id, "/page", "draft"), Is.Not.Null, "the write is under the caller's path");
        Assert.That((await page.TryGetValueAsync<string>("draft")).Value, Is.EqualTo("hello"));
    }

    [Test]
    public async Task APageReadsAnotherPagesKeyByPathAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/other", "draft", "\"left here\"");

        PageValue<string> found = await Scope(store, "/page", session).TryGetValueAsync<string>("/other", "draft");

        Assert.That(found.Found, Is.True);
        Assert.That(found.Value, Is.EqualTo("left here"));
    }

    [TestCase("/Other/")]
    [TestCase("/other/.")]
    [TestCase("/other/../other")]
    public async Task APathIsNormalisedBeforeItNamesAnotherPageAsync(string pagePath) {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/other", "draft", "\"left here\"");

        Assert.That((await Scope(store, "/page", session).TryGetValueAsync<string>(pagePath, "draft")).Value,
            Is.EqualTo("left here"));
    }

    [TestCase("/a?b")]
    [TestCase("/a#b")]
    public async Task APathThatCannotNameAPageIsRefusedAsync(string pagePath) {
        IPageSession page = Scope(new FakePageSessionStore(), "/page");

        await Assert.ThrowsAsync<ArgumentException>(() => page.TryGetValueAsync<string>(pagePath, "draft"));
    }

    [Test]
    public async Task APageCannotWriteOrDeleteUnderAnotherPathAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/other", "draft", "\"left here\"");
        IPageSession page = Scope(store, "/page", session);
        await page.SetAsync("draft", "mine");

        Assert.That(await page.DeleteAsync("draft"), Is.True);
        Assert.That(store.Raw(session.Id, "/other", "draft"), Is.Not.Null, "only the caller's own key went");
        Assert.That(await page.KeysAsync(), Is.Empty);
    }

    [Test]
    public async Task KeysAsyncListsOnlyTheCallersKeysAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/other", "theirs", "1");

        IPageSession page = Scope(store, "/page", session);
        await page.SetAsync("b", 1);
        await page.SetAsync("a", 2);

        Assert.That(await page.KeysAsync(), Is.EqualTo(new List<string> { "a", "b" }));
    }

    [Test]
    public async Task AllKeysAsyncListsPairsFromEveryPageAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/other", "theirs", "1");

        IPageSession page = Scope(store, "/page", session);
        await page.SetAsync("mine", 1);

        Assert.That(await page.AllKeysAsync(), Is.EqualTo(new List<PageSessionKey> {
            new("/other", "theirs"),
            new("/page", "mine"),
        }));
    }

    [Test]
    public async Task AHeadRenderMayReadButNotWriteAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/page", "visits", "1");
        PageSessionScope page = new(
            Key("/page"), store, TestOptions.Session, session, writable: false, slides: false);

        Assert.That((await page.TryGetValueAsync<int>("visits")).Value, Is.EqualTo(1));
        Assert.That(await page.KeysAsync(), Is.Not.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.SetAsync("visits", 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.DeleteAsync("visits"));
    }

    [Test]
    public async Task ALayoutMayNotWriteAsync() {
        FakePageSessionStore store = new();
        PageSessionScope page = new(
            Key("/_layout"), store, TestOptions.Session, NewSession(), writable: false, slides: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => page.SetAsync("draft", "x"));
    }

    [Test]
    public async Task AValueOverTheCapIsRefusedBeforeARowIsWrittenAsync() {
        FakePageSessionStore store = new();
        PageSessionOptions options = TestOptions.Session with {
            Anonymous = new PageSessionCaps(MaxValueBytes: 16, MaxKeys: 8, MaxTotalBytes: 1024),
        };
        IPageSession page = new PageSessionScope(
            Key("/page"), store, options, NewSession(), writable: true, slides: true);

        await Assert.ThrowsAsync<ArgumentException>(() => page.SetAsync("big", new string('x', 64)));
        Assert.That(store.ValueCount, Is.Zero);
    }

    [Test]
    public async Task AKeyLongerThanTheCapIsRefusedAsync() {
        PageSessionOptions options = TestOptions.Session with { MaxKeyChars = 4 };
        IPageSession page = new PageSessionScope(
            Key("/page"), new FakePageSessionStore(), options, NewSession(), writable: true, slides: true);

        await Assert.ThrowsAsync<ArgumentException>(() => page.SetAsync("12345", 1));
    }

    [TestCase("line\nbreak")]
    [TestCase("tab\tkey")]
    public async Task AKeyWithAControlCharacterIsRefusedAsync(string key) {
        IPageSession page = Scope(new FakePageSessionStore(), "/page");

        await Assert.ThrowsAsync<ArgumentException>(() => page.SetAsync(key, 1));
    }

    [Test]
    public async Task AWritePastTheKeyCountIsRefusedAsync() {
        FakePageSessionStore store = new();
        PageSessionOptions options = TestOptions.Session with {
            Anonymous = new PageSessionCaps(MaxValueBytes: 1024, MaxKeys: 2, MaxTotalBytes: 4096),
        };
        IPageSession page = new PageSessionScope(
            Key("/page"), store, options, NewSession(), writable: true, slides: true);

        await page.SetAsync("one", 1);
        await page.SetAsync("two", 2);

        await Assert.ThrowsAsync<ArgumentException>(() => page.SetAsync("three", 3));
    }

    [Test]
    public async Task AWritePastTheByteTotalIsRefusedAsync() {
        FakePageSessionStore store = new();
        PageSessionOptions options = TestOptions.Session with {
            Anonymous = new PageSessionCaps(MaxValueBytes: 1024, MaxKeys: 8, MaxTotalBytes: 64),
        };
        IPageSession page = new PageSessionScope(
            Key("/page"), store, options, NewSession(), writable: true, slides: true);

        await page.SetAsync("small", "1");

        await Assert.ThrowsAsync<ArgumentException>(() => page.SetAsync("big", new string('x', 100)));
        Assert.That((await page.TryGetValueAsync<string>("small")).Value, Is.EqualTo("1"));
    }

    [Test]
    public async Task ASignedInSessionUsesTheSignedInCapsAsync() {
        FakePageSessionStore store = new();
        PageSessionOptions options = TestOptions.Session with {
            Anonymous = new PageSessionCaps(MaxValueBytes: 16, MaxKeys: 8, MaxTotalBytes: 1024),
            SignedIn = new PageSessionCaps(MaxValueBytes: 4096, MaxKeys: 32, MaxTotalBytes: 16384),
        };
        PageSessionRequest signedIn = new(PageSessionCookie.Mint(), "owner", presented: false, exists: false);
        IPageSession page = new PageSessionScope(Key("/page"), store, options, signedIn, writable: true, slides: true);

        await page.SetAsync("big", new string('x', 1024));

        Assert.That((await page.TryGetValueAsync<string>("big")).Value, Has.Length.EqualTo(1024));
    }

    [Test]
    public async Task AReadSlidesTheSessionOncePerRequestAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest first = NewSession();
        await Scope(store, "/page", first).SetAsync("draft", "one");
        store.Clock.Now = DateTimeOffset.UnixEpoch.AddHours(1);

        // The next request opens its own handle on the same session and reads twice.
        PageSessionRequest second = new(first.Id, null, presented: true, exists: true);
        IPageSession page = Scope(store, "/page", second);
        _ = await page.TryGetValueAsync<string>("draft");
        _ = await page.TryGetValueAsync<string>("draft");

        PageSessionEntry? row = await store.FindAsync(first.Id);
        Assert.That(row?.UpdatedAt, Is.EqualTo(DateTimeOffset.UnixEpoch.AddHours(1).UtcDateTime), "one touch per request");
    }

    [Test]
    public async Task AHeadReadDoesNotSlideTheSessionAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/page", "visits", "1");
        PageSessionScope page = new(
            Key("/page"), store, TestOptions.Session, session, writable: false, slides: false);
        store.Clock.Now = DateTimeOffset.UnixEpoch.AddDays(1);

        _ = await page.TryGetValueAsync<int>("visits");

        PageSessionEntry? row = await store.FindAsync(session.Id);
        Assert.That(row?.UpdatedAt, Is.EqualTo(DateTimeOffset.UnixEpoch.UtcDateTime));
    }

    [Test]
    public async Task AMissingKeyIsNotFoundAsync() {
        PageValue<int> stored = await Scope(new FakePageSessionStore(), "/page").TryGetValueAsync<int>("absent");

        Assert.That(stored.Found, Is.False);
        Assert.That(stored.Value, Is.Zero);
    }

    [Test]
    public async Task AStoredNullIsFoundWithANullValueAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/page", "last", "null");

        PageValue<Entry?> stored = await Scope(store, "/page", session).TryGetValueAsync<Entry?>("last");

        Assert.That(stored.Found, Is.True);
        Assert.That(stored.Value, Is.Null);
    }

    [Test]
    public async Task AStoredValueOfTheWrongShapeThrowsRatherThanReportingAbsentAsync() {
        FakePageSessionStore store = new();
        PageSessionRequest session = NewSession();
        store.Seed(session.Id, "/page", "entry", "\"text\"");

        await Assert.ThrowsAsync<JsonException>(() => Scope(store, "/page", session).TryGetValueAsync<Entry>("entry"));
    }

    [Test]
    public async Task AnAbandonedRenderCannotUseTheSessionAsync() {
        PageSessionScope page = new(
            Key("/page"), new FakePageSessionStore(), TestOptions.Session, NewSession(), writable: true, slides: true);

        page.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(() => page.SetAsync("key", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.TryGetValueAsync<int>("key"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.KeysAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.AllKeysAsync());
    }

    [Test]
    public async Task TheRenderDeadlineIsPassedToTheStoreAsync() {
        FakePageSessionStore store = new();
        PageSessionScope page = new(
            Key("/page"), store, TestOptions.Session, NewSession(), writable: true, slides: true);
        using CancellationTokenSource deadline = new();
        page.AttachDeadline(deadline.Token);

        await page.SetAsync("key", 1);

        Assert.That(store.LastCancellationToken, Is.EqualTo(deadline.Token));
    }

    [Test]
    public async Task AnAlreadyCancelledDeadlineRefusesTheCallAsync() {
        FakePageSessionStore store = new();
        PageSessionScope page = new(
            Key("/page"), store, TestOptions.Session, NewSession(), writable: true, slides: true);
        using CancellationTokenSource deadline = new();
        await deadline.CancelAsync();
        page.AttachDeadline(deadline.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => page.SetAsync("key", 1));
    }

    [Test]
    public async Task AFirstWriteAfterTheCommitIsRefusedAsync() {
        FakePageSessionStore store = new();
        PageCommitState commit = new() { Committed = true };
        PageSessionRequest session = NewSession();
        IPageSession page = new PageSessionScope(
            Key("/page"), store, TestOptions.Session, session, writable: true, slides: true, commit: commit);

        InvalidOperationException? refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => page.SetAsync("draft", "hello"));

        Assert.Multiple(() => {
            Assert.That(refused?.Message, Does.Contain("before the first flush"));
            Assert.That(store.Raw(session.Id, "/page", "draft"), Is.Null);
        });
    }

    [Test]
    public async Task AWriteWithAnExistingSessionAfterTheCommitIsAllowedAsync() {
        FakePageSessionStore store = new();
        PageCommitState commit = new() { Committed = true };
        PageSessionRequest existing = new(PageSessionCookie.Mint(), null, presented: true, exists: true);
        store.Seed(existing.Id, "/page", "other", "\"x\"");
        IPageSession page = new PageSessionScope(
            Key("/page"), store, TestOptions.Session, existing, writable: true, slides: true, commit: commit);

        await page.SetAsync("draft", "hello");

        Assert.That(store.Raw(existing.Id, "/page", "draft"), Is.Not.Null);
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);

    private static PageSessionRequest NewSession(string? account = null) =>
        new(PageSessionCookie.Mint(), account, presented: false, exists: false);

    private static IPageSession Scope(FakePageSessionStore store, string pagePath, PageSessionRequest? session = null) =>
        new PageSessionScope(
            Key(pagePath),
            store,
            TestOptions.Session,
            session ?? NewSession(),
            writable: true,
            slides: true);

    private sealed record Entry(string Name, int Count);
}
