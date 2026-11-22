// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;
using HostPageKind = uk.osric.asp.Core.Pages.PageKind;

/// <summary>The handle a page reaches messages through: the address check, the lifetimes and the verbs.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PageMessageStoreTests {

    private const string SenderKey = "/sender";
    private const string ReceiverKey = "/receiver";

    private readonly string _sessionId = PageSessionCookie.Mint();
    private readonly FakeStoreMessages _messages = new();
    private readonly FakePageReadStore _pages = new();
    private readonly FakePageSessionStore _sessions = new();

    [SetUp]
    public void StoreThePages() {
        _pages.Save(SenderKey, "a sender", templateKind: HostPageKind.Page);
        _pages.Save(ReceiverKey, "a receiver", templateKind: HostPageKind.Page);
    }

    [Test]
    public async Task APagePostsToAnotherAndTheOtherTakesItAsync() {
        PageMessageStore sender = Store(SenderKey);
        PageMessageStore receiver = Store(ReceiverKey);

        PageMessagePostResult posted = await sender.PostAsync(ReceiverKey, "hello");
        PageMessageRead taken = await receiver.TakeAsync();

        Assert.Multiple(() => {
            Assert.That(posted.Posted, Is.True);
            Assert.That(taken.Found, Is.True);
            Assert.That(taken.Message?.From, Is.EqualTo(SenderKey));
            Assert.That(taken.Message?.Body, Is.EqualTo("hello"));
        });
        Assert.That((await receiver.TakeAsync()).Refusal, Is.EqualTo(PageMessageRefusal.Empty));
    }

    [Test]
    public async Task ATopicIsASeparateQueueAsync() {
        PageMessageStore sender = Store(SenderKey);
        PageTopic topic = PageTopic.Named("job");

        _ = await sender.PostAsync(topic, "on the topic");

        Assert.That((await Store(SenderKey).PeekAsync()).Refusal, Is.EqualTo(PageMessageRefusal.Empty));
        Assert.That((await Store(SenderKey).TakeAsync(topic)).Message?.Body, Is.EqualTo("on the topic"));
    }

    [Test]
    public void ATopicNameIsCanonicalisedAsAPageKeyIs() {
        Assert.Multiple(() => {
            Assert.That(PageTopic.Named("JOB").Name, Is.EqualTo("job"));
            Assert.That(PageTopic.Named("Caf\u00C9"), Is.EqualTo(PageTopic.Named("cafe\u0301")),
                "a composed and a decomposed spelling are one queue");
        });
    }

    [Test]
    public async Task TwoSpellingsOfATopicAreOneQueueAsync() {
        _ = await Store(SenderKey).PostAsync(PageTopic.Named("Help"), "hello");

        Assert.That((await Store(SenderKey).TakeAsync(PageTopic.Named("help"))).Message?.Body, Is.EqualTo("hello"));
    }

    [Test]
    public async Task AMissingDestinationIsUnknownPageAsync() {
        PageMessagePostResult result = await Store(SenderKey).PostAsync("/nowhere", "hello");

        Assert.That(result.Refusal, Is.EqualTo(PageMessageRefusal.UnknownPage));
    }

    [Test]
    public async Task AReservedKeyIsOwnedBySiteAsync() {
        PageMessagePostResult result = await Store(SenderKey).PostAsync("/login", "hello");

        Assert.That(result.Refusal, Is.EqualTo(PageMessageRefusal.OwnedBySite));
    }

    [Test]
    public async Task ALibraryIsNotASinkAsync() {
        _pages.Save("/lib", "a library", templateKind: HostPageKind.Library);

        Assert.That((await Store(SenderKey).PostAsync("/lib", "hello")).Refusal, Is.EqualTo(PageMessageRefusal.NotASink));
    }

    [Test]
    public async Task AContentOnlyPageIsNotASinkAsync() {
        _pages.Save("/note.txt", "text", templateKind: HostPageKind.Text);

        Assert.That((await Store(SenderKey).PostAsync("/note.txt", "hello")).Refusal,
            Is.EqualTo(PageMessageRefusal.NotASink));
    }

    [Test]
    public async Task AnUnaddressableKeyIsRefusedAsync() {
        PageMessagePostResult result = await Store(SenderKey).PostAsync("/receiver?x", "hello");

        Assert.That(result.Refusal, Is.EqualTo(PageMessageRefusal.NotAddressable));
    }

    [Test]
    public async Task AFullQueueAnswersFullAsync() {
        FakeStoreMessages messages = new();
        PageMessageStore sender = Store(SenderKey, messages: messages, caps: new PageMessageOptions(1024, 1, 1024));

        Assert.That((await sender.PostAsync(ReceiverKey, "one")).Posted, Is.True);
        Assert.That((await sender.PostAsync(ReceiverKey, "two")).Refusal, Is.EqualTo(PageMessageRefusal.Full));
    }

    [Test]
    public void AOneShotLifetimeIsAPageBug() {
        PageMessageStore store = Store(SenderKey);

        Assert.Multiple(() => {
            _ = Assert.ThrowsAsync<ArgumentException>(() => store.PostAsync(ReceiverKey, "hello", StoreLifetime.NextRead));
            _ = Assert.ThrowsAsync<ArgumentException>(() => store.PostAsync(ReceiverKey, "hello", StoreLifetime.Page));
        });
    }

    [Test]
    public async Task ASessionMessageIsTheVisitorsAsync() {
        PageMessageStore sender = Store(SenderKey);
        _ = await sender.PostAsync(ReceiverKey, "hello", StoreLifetime.Session);

        Assert.That(_sessions.Ids, Has.Count.EqualTo(1), "a Session post mints the session");
        Assert.That((await Store(ReceiverKey).TakeAsync()).Message?.Body, Is.EqualTo("hello"));
        Assert.That((await Store(ReceiverKey, reader: Reader(sessionId: "someone-else")).TakeAsync()).Refusal,
            Is.EqualTo(PageMessageRefusal.Empty));
    }

    [Test]
    public async Task ABrowserMessageMintsTheBrowserCookieAsync() {
        PageReaderSubject reader = Reader(browserId: null);
        PageMessageStore sender = Store(SenderKey, reader: reader);

        _ = await sender.PostAsync(ReceiverKey, "hello", StoreLifetime.Browser);

        Assert.Multiple(() => {
            Assert.That(reader.BrowserId, Is.Not.Null);
            Assert.That(reader.NeedsBrowserCookie, Is.True);
        });
    }

    [Test]
    public void AJobCannotCarryASessionLifetime() {
        PageReaderSubject reader = PageReaderSubject.ForJob(TestOptions.Stores.Visitor, "owner");
        PageMessageStore store = Store(SenderKey, reader: reader);

        _ = Assert.ThrowsAsync<ArgumentException>(() => store.PostAsync(ReceiverKey, "hello", StoreLifetime.Session));
    }

    [Test]
    public async Task ASignedInMessageIsOnTheAccountAsync() {
        PageMessageStore sender = Store(SenderKey, reader: Reader(account: "owner"));
        _ = await sender.PostAsync(ReceiverKey, "mine");

        PageMessageRead strangers = await Store(ReceiverKey, reader: Reader(account: "stranger")).TakeAsync();
        PageMessageRead owners = await Store(ReceiverKey, reader: Reader(account: "owner")).TakeAsync();

        Assert.Multiple(() => {
            Assert.That(strangers.Refusal, Is.EqualTo(PageMessageRefusal.Empty));
            Assert.That(owners.Message?.Body, Is.EqualTo("mine"));
        });
    }

    [Test]
    public async Task AHeadMayPeekButNotPostOrTakeAsync() {
        PageMessageStore reader = Store(ReceiverKey);
        _ = await Store(SenderKey).PostAsync(ReceiverKey, "hello");
        PageMessageStore head = Store(ReceiverKey, writable: false);

        Assert.That((await head.PeekAsync()).Message?.Body, Is.EqualTo("hello"));
        Assert.Multiple(() => {
            Assert.That(() => head.TakeAsync(), Throws.InvalidOperationException);
            Assert.That(() => head.PostAsync(ReceiverKey, "hello"), Throws.InvalidOperationException);
        });
        Assert.That((await reader.TakeAsync()).Message?.Body, Is.EqualTo("hello"), "the peek consumed nothing");
    }

    [Test]
    public async Task APeekDoesNotRearmTheJobsClockAsync() {
        int rearmed = 0;
        PageMessageStore store = Store(ReceiverKey, activity: () => rearmed++);

        _ = await store.PostAsync(ReceiverKey, "hello");
        Assert.That(rearmed, Is.EqualTo(1), "a post re-arms the silence clock");

        _ = await store.PeekAsync();
        Assert.That(rearmed, Is.EqualTo(1), "a peek is a look, not work");

        _ = await store.TakeAsync();
        Assert.That(rearmed, Is.EqualTo(2), "a take re-arms the silence clock");
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

    private PageMessageStore Store(
        string key,
        PageReaderSubject? reader = null,
        bool writable = true,
        FakeStoreMessages? messages = null,
        PageMessageOptions? caps = null,
        Action? activity = null) =>
        new(
            PagePath.Normalize(key),
            messages ?? _messages,
            _sessions,
            reader ?? Reader(),
            _pages,
            new ReservedPaths(ReservedPaths.Default),
            caps ?? TestOptions.Stores.Messages,
            writable,
            commit: null,
            activity);
}
