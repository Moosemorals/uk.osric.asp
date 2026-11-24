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
        Visitor first = Open();
        Visitor second = Open();

        await first.Store.SetAsync("draft", "one");

        Assert.That((await second.Store.TryGetValueAsync<string>("draft")).Found, Is.False);
        Assert.That(await second.Store.KeysAsync(), Is.Empty);
    }

    [Test]
    public async Task ASessionValueIsWrittenUnderTheCallersSubjectAndPathAsync() {
        Visitor visitor = Open();

        await visitor.Store.SetAsync("draft", "hello");

        Assert.That(visitor.Values.Raw(StoreSubjectKeys.ForSession(visitor.Session.Id), "/page", "draft"), Is.Not.Null);
        Assert.That((await visitor.Store.TryGetValueAsync<string>("draft")).Value, Is.EqualTo("hello"));
    }

    [Test]
    public async Task APageReadsAnotherPagesKeyByPathAsync() {
        Visitor visitor = Open();
        visitor.Values.SeedVisitor(StoreSubjectKeys.ForSession(visitor.Session.Id), "/other", "draft", "\"left here\"", StoreLifetimeKind.Session);

        PageValue<string> found = await visitor.Store.TryGetValueAsync<string>("/other", "draft");

        Assert.That(found.Found, Is.True);
        Assert.That(found.Value, Is.EqualTo("left here"));
        Assert.That(found.Page, Is.EqualTo("/other"));
    }

    [TestCase("/Other/")]
    [TestCase("/other/.")]
    [TestCase("/other/../other")]
    public async Task APathIsNormalisedBeforeItNamesAnotherPageAsync(string pagePath) {
        Visitor visitor = Open();
        visitor.Values.SeedVisitor(StoreSubjectKeys.ForSession(visitor.Session.Id), "/other", "draft", "\"left here\"", StoreLifetimeKind.Session);

        Assert.That((await visitor.Store.TryGetValueAsync<string>(pagePath, "draft")).Value, Is.EqualTo("left here"));
    }

    [TestCase("/a?b")]
    [TestCase("/a#b")]
    public async Task APathThatCannotNameAPageIsRefusedAsync(string pagePath) {
        Visitor visitor = Open();

        await Assert.ThrowsAsync<ArgumentException>(() => visitor.Store.TryGetValueAsync<string>(pagePath, "draft"));
    }

    [Test]
    public async Task APageCannotWriteOrDeleteUnderAnotherPathAsync() {
        Visitor visitor = Open();
        visitor.Values.SeedVisitor(StoreSubjectKeys.ForSession(visitor.Session.Id), "/other", "draft", "\"left here\"", StoreLifetimeKind.Session);
        await visitor.Store.SetAsync("draft", "mine");

        Assert.That(await visitor.Store.DeleteAsync("draft"), Is.True);
        Assert.That(visitor.Values.Raw(StoreSubjectKeys.ForSession(visitor.Session.Id), "/other", "draft"), Is.Not.Null,
            "only the caller's own key went");
        Assert.That(await visitor.Store.KeysAsync(), Is.Empty);
    }

    [Test]
    public async Task KeysAsyncListsOnlyTheCallersKeysAsync() {
        Visitor visitor = Open();
        visitor.Values.SeedVisitor(StoreSubjectKeys.ForSession(visitor.Session.Id), "/other", "theirs", "1", StoreLifetimeKind.Session);
        await visitor.Store.SetAsync("b", 1);
        await visitor.Store.SetAsync("a", 2);

        Assert.That(Keys(await visitor.Store.KeysAsync()), Is.EqualTo(new List<string> { "a", "b" }));
    }

    [Test]
    public async Task AllKeysAsyncListsPairsFromEveryPageAsync() {
        Visitor visitor = Open();
        visitor.Values.SeedVisitor(StoreSubjectKeys.ForSession(visitor.Session.Id), "/other", "theirs", "1", StoreLifetimeKind.Session);
        await visitor.Store.SetAsync("mine", 1);

        Assert.That(Pairs(await visitor.Store.AllKeysAsync()), Is.EqualTo(new List<PageSessionKey> {
            new("/other", "theirs"),
            new("/page", "mine"),
        }));
    }

    [Test]
    public async Task AListingCanSelectOneLifetimeAndBoundItsLengthAsync() {
        Visitor visitor = Open();
        await visitor.Store.SetAsync("person", 1, StoreLifetime.Person);
        await visitor.Store.SetAsync("session", 2, StoreLifetime.Session);
        await visitor.Store.SetAsync("browser", 3, StoreLifetime.Browser);

        await Assert.MultipleAsync(async () => {
            Assert.That(Keys(await visitor.Store.KeysAsync(new StoreFilter(StoreLifetimeKind.Session))),
                Is.EqualTo(new List<string> { "session" }));
            Assert.That(await visitor.Store.KeysAsync(new StoreFilter(Limit: 1)), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AHeadRenderMayReadButNotWriteAsync() {
        Visitor open = Open();
        await open.Store.SetAsync("visits", 1);
        Visitor head = Open(open.Session.Id, writable: false, state: open.Values, sessionStore: open.Sessions);

        Assert.That((await head.Store.TryGetValueAsync<int>("visits")).Value, Is.EqualTo(1));
        Assert.That(await head.Store.KeysAsync(), Is.Not.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(() => head.Store.SetAsync("visits", 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => head.Store.DeleteAsync("visits"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => head.Store.TryTakeAsync<int>("visits"));
    }

    [Test]
    public async Task ALayoutMayNotWriteAsync() {
        Visitor visitor = Open(writable: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => visitor.Store.SetAsync("draft", "x"));
    }

    [Test]
    public async Task AValueOverTheLimitIsRefusedBeforeARowIsWrittenAsync() {
        Visitor visitor = Open(options: Limits(anonymous: (16, 8, 1024)));

        await Assert.ThrowsAsync<ArgumentException>(() => visitor.Store.SetAsync("big", new string('x', 64)));
        Assert.That(visitor.Values.Count(StoreSubjectKeys.ForSession(visitor.Session.Id)), Is.Zero);
    }

    [Test]
    public async Task AKeyLongerThanTheLimitIsRefusedAsync() {
        PageStoresOptions options = TestOptions.Stores with { MaxKeyBytes = 4 };
        Visitor visitor = Open(options: options);

        await Assert.ThrowsAsync<ArgumentException>(() => visitor.Store.SetAsync("12345", 1));
    }

    [TestCase("line\nbreak")]
    [TestCase("tab\tkey")]
    public async Task AKeyWithAControlCharacterIsRefusedAsync(string key) {
        Visitor visitor = Open();

        await Assert.ThrowsAsync<ArgumentException>(() => visitor.Store.SetAsync(key, 1));
    }

    [Test]
    public async Task AWritePastTheKeyCountIsRefusedAsync() {
        Visitor visitor = Open(options: Limits(anonymous: (1024, 2, 4096)));

        await visitor.Store.SetAsync("one", 1);
        await visitor.Store.SetAsync("two", 2);

        await Assert.ThrowsAsync<ArgumentException>(() => visitor.Store.SetAsync("three", 3));
    }

    [Test]
    public async Task AWritePastTheByteTotalIsRefusedAsync() {
        Visitor visitor = Open(options: Limits(anonymous: (1024, 8, 64)));
        await visitor.Store.SetAsync("small", "1");

        await Assert.ThrowsAsync<ArgumentException>(() => visitor.Store.SetAsync("big", new string('x', 100)));
        Assert.That((await visitor.Store.TryGetValueAsync<string>("small")).Value, Is.EqualTo("1"));
    }

    [Test]
    public async Task ASignedInVisitorUsesTheSignedInLimitAsync() {
        Visitor visitor = Open(account: "owner", options: Limits(
            anonymous: (16, 8, 1024), signedIn: (4096, 32, 16384)));

        await visitor.Store.SetAsync("big", new string('x', 1024));

        Assert.That((await visitor.Store.TryGetValueAsync<string>("big")).Value, Has.Length.EqualTo(1024));
    }

    [Test]
    public async Task AReadSlidesTheSessionOncePerRequestAsync() {
        Visitor visitor = Open();
        await visitor.Store.SetAsync("draft", "one");
        visitor.Sessions.Clock.Now = DateTimeOffset.UnixEpoch.AddHours(1);

        _ = await visitor.Store.TryGetValueAsync<string>("draft");
        _ = await visitor.Store.TryGetValueAsync<string>("draft");

        Assert.That((await visitor.Sessions.FindAsync(visitor.Session.Id))!.UpdatedAt, Is.EqualTo(visitor.Sessions.Clock.GetUtcNow().UtcDateTime));
    }

    [Test]
    public async Task AFirstSessionWriteAfterTheCommitIsRefusedAsync() {
        Visitor visitor = Open(commit: new PageCommitState { Committed = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => visitor.Store.SetAsync("key", 1, StoreLifetime.Session));
    }

    [Test]
    public async Task AFirstBrowserWriteAfterTheCommitIsRefusedAsync() {
        Visitor visitor = Open(commit: new PageCommitState { Committed = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => visitor.Store.SetAsync("mode", "vim", StoreLifetime.Browser));
    }

    [Test]
    public async Task APersonValueSurvivesTheSessionWhenSignedInAsync() {
        Visitor first = Open(account: "owner");
        await first.Store.SetAsync("preference", "dark", StoreLifetime.Person);

        Visitor second = Open(account: "owner", state: first.Values, sessionStore: first.Sessions);

        Assert.That((await second.Store.TryGetValueAsync<string>("preference")).Value, Is.EqualTo("dark"));
    }

    [Test]
    public async Task AnAnonymousPersonValueDiesWithTheSessionAsync() {
        Visitor first = Open();
        await first.Store.SetAsync("preference", "dark", StoreLifetime.Person);

        Visitor second = Open(state: first.Values, sessionStore: first.Sessions);

        Assert.That((await second.Store.TryGetValueAsync<string>("preference")).Found, Is.False);
    }

    [Test]
    public async Task ASessionValueDiesWhenTheSessionRotatesAsync() {
        Visitor visitor = Open();
        await visitor.Store.SetAsync("draft", "one", StoreLifetime.Session);

        Assert.That(await visitor.Sessions.DeleteAsync(visitor.Session.Id), Is.True);

        Visitor rotated = Open(state: visitor.Values, sessionStore: visitor.Sessions);
        Assert.That((await rotated.Store.TryGetValueAsync<string>("draft")).Found, Is.False);
    }

    [Test]
    public async Task ABrowserValueSurvivesSigningInAndOutAndAnotherBrowserDoesNotSeeItAsync() {
        string browserId = PageBrowserCookie.Mint();
        Visitor anonymous = Open(browserId: browserId);
        await anonymous.Store.SetAsync("mode", "vim", StoreLifetime.Browser);

        Visitor signedIn = Open(account: "owner", browserId: browserId, state: anonymous.Values, sessionStore: anonymous.Sessions);
        Assert.That((await signedIn.Store.TryGetValueAsync<string>("mode")).Value, Is.EqualTo("vim"));

        Visitor signedOut = Open(browserId: browserId, state: anonymous.Values, sessionStore: anonymous.Sessions);
        Assert.That((await signedOut.Store.TryGetValueAsync<string>("mode")).Value, Is.EqualTo("vim"));

        Visitor another = Open(browserId: PageBrowserCookie.Mint());
        Assert.That((await another.Store.TryGetValueAsync<string>("mode")).Found, Is.False);
    }

    [Test]
    public async Task TheMoreEphemeralValueShadowsTheLongerLivedOneAsync() {
        string browserId = PageBrowserCookie.Mint();
        Visitor visitor = Open(account: "owner", browserId: browserId);

        await visitor.Store.SetAsync("key", "person", StoreLifetime.Person);
        await visitor.Store.SetAsync("key", "browser", StoreLifetime.Browser);
        await visitor.Store.SetAsync("key", "session", StoreLifetime.Session);

        Assert.That((await visitor.Store.TryGetValueAsync<string>("key")).Value, Is.EqualTo("session"));
        Assert.That((await visitor.Store.TryGetValueAsync<string>("key")).Lifetime.Kind, Is.EqualTo(StoreLifetimeKind.Session));

        _ = await visitor.Store.DeleteAsync("key");
        Assert.That((await visitor.Store.TryGetValueAsync<string>("key")).Value, Is.EqualTo("browser"));

        _ = await visitor.Store.DeleteAsync("key");
        Assert.That((await visitor.Store.TryGetValueAsync<string>("key")).Value, Is.EqualTo("person"));
    }

    [Test]
    public async Task AnAnonymousOneShotSurvivesAdoptionUnreadAsync() {
        Visitor anonymous = Open();
        await anonymous.Store.SetAsync("notice", "saved", StoreLifetime.NextRead);

        string next = PageSessionCookie.Mint();
        Assert.That(await anonymous.Sessions.AdoptAsync(anonymous.Session.Id, "owner", next), Is.True);

        Visitor signedIn = Open(next, "owner", state: anonymous.Values, sessionStore: anonymous.Sessions);
        Assert.That((await signedIn.Store.TryTakeAsync<string>("notice")).Value, Is.EqualTo("saved"));
        Assert.That((await signedIn.Store.TryTakeAsync<string>("notice")).Found, Is.False);
    }

    [Test]
    public async Task ABrowserWriteMintsTheCookieOnceAsync() {
        Visitor visitor = Open();

        await visitor.Store.SetAsync("mode", "vim", StoreLifetime.Browser);

        Assert.Multiple(() => {
            Assert.That(visitor.Reader.BrowserId, Is.Not.Null);
            Assert.That(visitor.Reader.NeedsBrowserCookie, Is.True);
        });
    }

    [Test]
    public async Task APageLifetimeIsRefusedOnAVisitorAsync() {
        Visitor visitor = Open();

        await Assert.ThrowsAsync<ArgumentException>(() => visitor.Store.SetAsync("key", 1, StoreLifetime.Page));
    }

    [Test]
    public async Task AnExpiredValueIsNotFoundAsync() {
        Visitor visitor = Open();
        await visitor.Store.SetAsync("notice", "old", StoreLifetime.Until(DateTimeOffset.UnixEpoch.AddSeconds(-1)));

        Assert.That((await visitor.Store.TryGetValueAsync<string>("notice")).Found, Is.False);
    }

    [Test]
    public async Task TheSessionAdapterSeesAndWritesOnlySessionValuesAsync() {
        Visitor visitor = Open();
        PageValueStore sessionOnly = visitor.Store.RestrictedTo(StoreLifetimeKind.Session);
        visitor.Values.SeedVisitor(StoreSubjectKeys.ForSession(visitor.Session.Id), "/page", "browser", "\"x\"", StoreLifetimeKind.Browser);

        Assert.That((await sessionOnly.TryGetValueAsync<string>("browser")).Found, Is.False);
        await sessionOnly.SetAsync("draft", "one");
        Assert.That((await sessionOnly.TryGetValueAsync<string>("draft")).Value, Is.EqualTo("one"));
        Assert.That((await visitor.Store.TryGetValueAsync<string>("draft")).Lifetime.Kind, Is.EqualTo(StoreLifetimeKind.Session));
    }

    [Test]
    public async Task AJobVisitorHasOnlyPersonAsync() {
        FakeStoreValues values = new();
        FakePageSessionStore sessions = new(values);
        PageReaderSubject reader = PageReaderSubject.ForJob(TestOptions.Stores.Visitor, "owner");
        PageValueStore store = new(Key("/page"), values, sessions, reader, TestOptions.Stores.MaxKeyBytes, writable: true, commit: null);

        await store.SetAsync("state", 1);
        Assert.That((await store.TryGetValueAsync<int>("state")).Value, Is.EqualTo(1));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("key", 1, StoreLifetime.Session));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync("key", 1, StoreLifetime.Browser));
    }

    [Test]
    public async Task AnAbandonedRenderCannotUseTheStoreAsync() {
        Visitor visitor = Open();

        visitor.Store.Abandon();

        await Assert.ThrowsAsync<InvalidOperationException>(() => visitor.Store.SetAsync("key", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => visitor.Store.TryGetValueAsync<int>("key"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => visitor.Store.KeysAsync());
    }

    [Test]
    public async Task TheRenderDeadlineIsPassedToTheStoreAsync() {
        Visitor visitor = Open();
        using CancellationTokenSource deadline = new();
        visitor.Store.AttachDeadline(deadline.Token);

        await visitor.Store.SetAsync("key", 1);

        Assert.That(visitor.Values.LastCancellationToken, Is.EqualTo(deadline.Token));
    }

    [Test]
    public async Task AnAlreadyCancelledDeadlineRefusesTheCallAsync() {
        Visitor visitor = Open();
        using CancellationTokenSource deadline = new();
        await deadline.CancelAsync();
        visitor.Store.AttachDeadline(deadline.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => visitor.Store.SetAsync("key", 1));
    }

    [Test]
    public async Task AStoredValueOfTheWrongShapeThrowsRatherThanReportingAbsentAsync() {
        Visitor visitor = Open();
        visitor.Values.SeedVisitor(StoreSubjectKeys.ForSession(visitor.Session.Id), "/page", "entry", "\"text\"", StoreLifetimeKind.Session);

        await Assert.ThrowsAsync<JsonException>(() => visitor.Store.TryGetValueAsync<Entry>("entry"));
    }

    private static List<string> Keys(IReadOnlyList<StoreEntry> entries) => [.. entries.Select(entry => entry.Key)];

    private static List<PageSessionKey> Pairs(IReadOnlyList<StoreEntry> entries) =>
        [.. entries.Select(entry => new PageSessionKey(entry.Page, entry.Key))];

    private static PageStoresOptions Limits(
        (int Value, int Keys, int Total) anonymous,
        (int Value, int Keys, int Total)? signedIn = null) {
        SubjectLimits anonymousLimits = new(anonymous.Value, anonymous.Keys, anonymous.Total);
        (int Value, int Keys, int Total) signed = signedIn ?? (4096, 32, 16384);
        SubjectLimits signedInLimits = new(signed.Value, signed.Keys, signed.Total);

        return TestOptions.Stores with {
            Visitor = TestOptions.Stores.Visitor with {
                Anonymous = anonymousLimits,
                Browser = anonymousLimits with { MaxValueBytes = signedInLimits.MaxValueBytes, MaxTotalBytes = signedInLimits.MaxTotalBytes },
                SignedIn = signedInLimits,
            },
        };
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);

    private static Visitor Open(
        string? sessionId = null,
        string? account = null,
        string? browserId = null,
        PageStoresOptions? options = null,
        bool writable = true,
        PageCommitState? commit = null,
        FakeStoreValues? state = null,
        FakePageSessionStore? sessionStore = null) {
        PageStoresOptions stores = options ?? TestOptions.Stores;
        FakeStoreValues values = state ?? new FakeStoreValues(stores);
        FakePageSessionStore sessions = sessionStore ?? new FakePageSessionStore(values, stores.Visitor);
        PageSessionRequest session = sessionId is null
            ? new PageSessionRequest(PageSessionCookie.Mint(), account, presented: false, exists: false)
            : new PageSessionRequest(sessionId, account, presented: true, exists: true);
        PageReaderSubject reader = new(session, stores.Visitor, browserId);
        PageValueStore store = new(
            Key("/page"), values, sessions, reader, stores.MaxKeyBytes, writable, commit);

        return new Visitor(store, values, sessions, session, reader);
    }

    private sealed record Visitor(
        PageValueStore Store,
        FakeStoreValues Values,
        FakePageSessionStore Sessions,
        PageSessionRequest Session,
        PageReaderSubject Reader);

    private sealed record Entry(string Name, int Count);
}
