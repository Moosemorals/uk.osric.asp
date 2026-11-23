// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class SqliteStoreMessagesTests {

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;
    private TestClock _clock = new();

    [OneTimeSetUp]
    public static void MigrateOnce() => s_template = new DatabaseTemplate("messages");

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("messages");
        _options = DatabaseTemplate.Options(_databasePath);
        _clock = new TestClock();
    }

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task ATakeRemovesTheMessageAndASecondTakeFindsNothingAsync() {
        IStoreMessages store = Messages();
        Assert.That(await PostAsync(store, "/sender", "hello", to: "/receiver"), Is.True);

        StoreMessageRow? taken = await store.TakeAsync(Subjects("session:one"), "/receiver", null);

        Assert.Multiple(() => {
            Assert.That(taken?.Body, Is.EqualTo("hello"));
            Assert.That(taken?.FromPageKey, Is.EqualTo("/sender"));
        });
        Assert.That(await store.TakeAsync(Subjects("session:one"), "/receiver", null), Is.Null);
    }

    [Test]
    public async Task TheOldestMessageIsTakenFirstAsync() {
        IStoreMessages store = Messages();
        _ = await PostAsync(store, "/sender", "first", to: "/receiver");
        _ = await PostAsync(store, "/sender", "second", to: "/receiver");

        Assert.That((await store.TakeAsync(Subjects("session:one"), "/receiver", null))?.Body, Is.EqualTo("first"));
        Assert.That((await store.TakeAsync(Subjects("session:one"), "/receiver", null))?.Body, Is.EqualTo("second"));
    }

    [Test]
    public async Task APeekAnswersWhatATakeWouldAndTakesNothingAsync() {
        IStoreMessages store = Messages();
        _ = await PostAsync(store, "/sender", "hello", to: "/receiver");

        StoreMessageRow? peeked = await store.PeekAsync(Subjects("session:one"), "/receiver", null);
        StoreMessageRow? taken = await store.TakeAsync(Subjects("session:one"), "/receiver", null);

        Assert.Multiple(() => {
            Assert.That(peeked?.Body, Is.EqualTo("hello"));
            Assert.That(taken?.Body, Is.EqualTo("hello"), "the peek removed nothing");
        });
    }

    [Test]
    public async Task ATopicIsASeparateQueueAsync() {
        IStoreMessages store = Messages();
        _ = await PostAsync(store, "/sender", "on the page", to: "/receiver");
        _ = await PostAsync(store, "/sender", "on the topic", topic: "job");

        Assert.That((await store.TakeAsync(Subjects("session:one"), "/receiver", null))?.Body, Is.EqualTo("on the page"));
        Assert.That((await store.TakeAsync(Subjects("session:one"), "/receiver", null)), Is.Null);
        Assert.That((await store.TakeAsync(Subjects("session:one"), null, "job"))?.Body, Is.EqualTo("on the topic"));
    }

    [Test]
    public async Task AnotherSubjectCannotTakeTheMessageAsync() {
        IStoreMessages store = Messages();
        _ = await PostAsync(store, "/sender", "mine", to: "/receiver");

        Assert.That(await store.TakeAsync(Subjects("session:two"), "/receiver", null), Is.Null);
        Assert.That((await store.TakeAsync(Subjects("session:one"), "/receiver", null))?.Body, Is.EqualTo("mine"));
    }

    [Test]
    public async Task AnExpiredMessageIsNotTakenAsync() {
        IStoreMessages store = Messages();
        _ = await store.PostAsync(
            Write("session:one", "/sender", "old", to: "/receiver", expiresAt: StoreSubjectKeys.Stamp(_clock.GetUtcNow().AddSeconds(-1))),
            MessagesOptions());

        Assert.That(await store.TakeAsync(Subjects("session:one"), "/receiver", null), Is.Null);
    }

    [Test]
    public async Task TheCountLimitRefusesAPostAsync() {
        IStoreMessages store = Messages();
        PageMessageOptions limits = MessagesOptions() with { MaxMessagesPerSubject = 1 };
        Assert.That(await store.PostAsync(Write("session:one", "/sender", "one", to: "/receiver"), limits), Is.True);

        Assert.That(await store.PostAsync(Write("session:one", "/sender", "two", to: "/receiver"), limits), Is.False);
        Assert.That((await store.TakeAsync(Subjects("session:one"), "/receiver", null))?.Body, Is.EqualTo("one"));
    }

    [Test]
    public async Task TheByteLimitRefusesAPostAsync() {
        IStoreMessages store = Messages();
        PageMessageOptions limits = MessagesOptions() with { MaxSubjectBytes = 8 };

        Assert.That(await store.PostAsync(Write("session:one", "/sender", "12345678", to: "/receiver"), limits), Is.True);
        Assert.That(await store.PostAsync(Write("session:one", "/sender", "9", to: "/receiver"), limits), Is.False);
    }

    [Test]
    public async Task TwoConcurrentTakesAnswerOneMessageBetweenThemAsync() {
        IStoreMessages store = Messages();
        _ = await PostAsync(store, "/sender", "only one", to: "/receiver");

        StoreMessageRow?[] taken = await Task.WhenAll(
            store.TakeAsync(Subjects("session:one"), "/receiver", null),
            store.TakeAsync(Subjects("session:one"), "/receiver", null));

        Assert.That(taken.Count(row => row is not null), Is.EqualTo(1));
    }

    [Test]
    public async Task TheSweepRemovesExpiredMessagesAndAQuietBrowserAsync() {
        IStoreMessages store = Messages();
        _ = await store.PostAsync(
            Write("session:one", "/sender", "old", to: "/receiver", expiresAt: StoreSubjectKeys.Stamp(_clock.GetUtcNow().AddSeconds(-1))),
            MessagesOptions());
        _ = await PostAsync(store, "/sender", "quiet", subject: "browser:quiet", to: "/receiver");
        _ = await PostAsync(store, "/sender", "live", subject: "browser:live", to: "/receiver");

        _clock.Now = _clock.Now.AddDays(2);
        _ = await store.PostAsync(Write("browser:live", "/sender", "newer", to: "/receiver"), MessagesOptions());

        int removed = await store.RemoveExpiredAsync(TimeSpan.FromDays(1));
        StoreMessageRow? quiet = await store.TakeAsync(Subjects("browser:quiet"), "/receiver", null);
        StoreMessageRow? live = await store.TakeAsync(Subjects("browser:live"), "/receiver", null);

        Assert.Multiple(() => {
            Assert.That(removed, Is.EqualTo(2), "the expired message and the quiet browser's");
            Assert.That(quiet, Is.Null);
            Assert.That(live?.Body, Is.EqualTo("live"));
        });
    }

    [Test]
    public async Task DeletingAPageTakesItsMessagesAsync() {
        IStoreMessages store = Messages();
        _ = await PostAsync(store, "/sender", "hello", to: "/receiver");
        await DatabaseTemplate.SavePageAsync(_options, "/receiver");

        await using (StorageDbContext database = new(_options)) {
            await new SqlitePageStore(database, Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlitePageStore>.Instance)
                .DeletePageAsync(PagePath.Normalize("/receiver"));
        }

        Assert.That(await store.TakeAsync(Subjects("session:one"), "/receiver", null), Is.Null);
    }

    [Test]
    public async Task MovingAPageRekeysItsMessagesAsync() {
        IStoreMessages store = Messages();
        _ = await PostAsync(store, "/sender", "hello", to: "/old");
        await DatabaseTemplate.SavePageAsync(_options, "/old");

        await using (StorageDbContext database = new(_options)) {
            PageMoveStoreResult moved = await new SqlitePageStore(
                    database,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlitePageStore>.Instance)
                .MovePageAsync(PagePath.Normalize("/old"), PagePath.Normalize("/new"), ExpectedHash(database));
            Assert.That(moved.Outcome, Is.EqualTo(PageMoveStoreOutcome.Moved));
        }

        Assert.That(await store.TakeAsync(Subjects("session:one"), "/old", null), Is.Null);
        Assert.That((await store.TakeAsync(Subjects("session:one"), "/new", null))?.Body, Is.EqualTo("hello"));
    }

    [Test]
    public async Task AdoptionRekeysPersonMessagesAndLeavesTheSessionsBehindAsync() {
        IStoreMessages store = Messages();
        _ = await store.PostAsync(Write("session:one", "/sender", "person", to: "/receiver"), MessagesOptions());
        _ = await store.PostAsync(
            Write("session:one", "/sender", "session", to: "/receiver") with { Lifetime = (int)StoreLifetimeKind.Session },
            MessagesOptions());

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.AdoptAsync(database, "one", "owner", CancellationToken.None);
            _ = await StoreValueSql.DeleteSubjectAsync(database, "session:one", CancellationToken.None);
        }

        StoreMessageRow? adopted = await store.TakeAsync(Subjects("account:owner"), "/receiver", null);
        StoreMessageRow? ended = await store.TakeAsync(Subjects("account:owner"), "/receiver", null);

        Assert.Multiple(() => {
            Assert.That(adopted?.Body, Is.EqualTo("person"), "a Person message is adopted into the account");
            Assert.That(ended, Is.Null, "a Session message dies with the session");
        });
    }

    private static IReadOnlyList<string> Subjects(string subject) => [subject];

    private static PageMessageOptions MessagesOptions() => TestOptions.Stores.Messages;

    private static StoreMessageWrite Write(
        string subject,
        string from,
        string body,
        string? to = null,
        string? topic = null,
        string? expiresAt = null) =>
        new(subject, to, topic, from, body, (int)StoreLifetimeKind.Person, expiresAt);

    private static Task<bool> PostAsync(
        IStoreMessages store,
        string from,
        string body,
        string? to = null,
        string? topic = null,
        string subject = "session:one") =>
        store.PostAsync(Write(subject, from, body, to, topic), MessagesOptions());

    private static string ExpectedHash(StorageDbContext database) =>
        database.Set<PageHead>().AsNoTracking()
            .Where(head => head.Path == "/old")
            .Join(database.Set<PageRevision>().AsNoTracking(),
                head => new { head.RevisionId, head.Path },
                revision => new { revision.RevisionId, revision.Path },
                (_, revision) => revision.Hash)
            .Single();

    private IStoreMessages Messages() => new SqliteStoreMessages(DatabaseTemplate.Factory(_options), TestOptions.Stores, _clock);
}
