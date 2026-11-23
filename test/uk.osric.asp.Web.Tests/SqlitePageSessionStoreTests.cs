// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using System.Globalization;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
// Each test owns its storage - a database file of its own, or a store and harness of its own - so the
// tests here are independent, and waiting for one to finish before starting the next only makes the
// suite slower than the work it is doing.
[Parallelizable(ParallelScope.All)]
public sealed class SqlitePageSessionStoreTests {

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("session");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("session");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task ASessionSurvivesReopeningTheDatabaseAsync() {
        IPageSessionStore store = Store();
        Assert.That(await store.EnsureAsync("session-id", account: null), Is.True);

        Assert.That(await Store().FindAsync("session-id"), Is.Not.Null);
    }

    [Test]
    public async Task TouchSlidesTheTimestampAsync() {
        TestClock clock = new();
        IPageSessionStore store = Store(clock);
        _ = await store.EnsureAsync("session-id", account: null);
        clock.Now = DateTimeOffset.UnixEpoch.AddHours(1);

        await store.TouchAsync("session-id");

        PageSessionEntry? row = await Store(clock).FindAsync("session-id");
        Assert.That(row?.UpdatedAt, Is.EqualTo(DateTimeOffset.UnixEpoch.AddHours(1).UtcDateTime));
    }

    [Test]
    public async Task DeleteReportsWhetherTheSessionExistedAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);

        Assert.That(await store.DeleteAsync("session-id"), Is.True);
        Assert.That(await store.DeleteAsync("session-id"), Is.False);
        Assert.That(await store.FindAsync("session-id"), Is.Null);
    }

    [Test]
    public async Task RemoveExpiredDropsThePastItsIdleWindowAndLeavesTheRestAsync() {
        TestClock clock = new();
        IPageSessionStore store = Store(clock);
        _ = await store.EnsureAsync("anonymous", account: null);
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(20);
        _ = await store.EnsureAsync("signed-in", account: "owner");
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(31);

        PageSessionSweep sweep = await store.RemoveExpiredAsync();

        Assert.That(sweep.Expired, Is.EqualTo(1));
        Assert.That(await store.FindAsync("anonymous"), Is.Null);
        Assert.That(await store.FindAsync("signed-in"), Is.Not.Null, "a signed-in session lives as long as its sign-in");
    }

    [Test]
    public async Task ASignedInSessionOutlivesTheAnonymousWindowAsync() {
        TestClock clock = new();
        IPageSessionStore store = Store(clock);
        _ = await store.EnsureAsync("signed-in", account: "owner");
        clock.Now = DateTimeOffset.UnixEpoch.AddDays(29);

        PageSessionSweep sweep = await store.RemoveExpiredAsync();

        Assert.That(sweep.Expired, Is.Zero);
        Assert.That(await store.FindAsync("signed-in"), Is.Not.Null);

        clock.Now = DateTimeOffset.UnixEpoch.AddDays(31);
        _ = await store.RemoveExpiredAsync();
        Assert.That(await store.FindAsync("signed-in"), Is.Null);
    }

    [Test]
    public async Task AMintRemovesExpiredRowsBeforeItEvictsALiveSessionAsync() {
        TestClock clock = new();
        IPageSessionStore store = Store(clock, TestOptions.Stores.Visitor with { MaxLiveSessions = 2 });
        _ = await store.EnsureAsync("stale", account: null);
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        _ = await store.EnsureAsync("live", account: "owner");
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(31);

        Assert.That(await store.EnsureAsync("new", account: null), Is.True);

        Assert.That(await store.FindAsync("stale"), Is.Null, "expiry is what made room");
        Assert.That(await store.FindAsync("live"), Is.Not.Null, "no live session was evicted");
        Assert.That(await store.FindAsync("new"), Is.Not.Null);
    }

    [Test]
    public async Task TheCapEvictsTheMostIdleAnonymousSessionFirstAsync() {
        TestClock clock = new();
        IPageSessionStore store = Store(clock, TestOptions.Stores.Visitor with { MaxLiveSessions = 2 });
        _ = await store.EnsureAsync("older-stranger", account: null);
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        _ = await store.EnsureAsync("newer-stranger", account: null);
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(2);
        _ = await store.EnsureAsync("owner", account: "owner");
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(3);

        Assert.That(await store.EnsureAsync("another", account: null), Is.True);

        Assert.That(await store.FindAsync("older-stranger"), Is.Null);
        Assert.That(await store.FindAsync("owner"), Is.Not.Null, "a flood must not cost the owner their session");
    }

    [Test]
    public async Task WithNoAnonymousSessionLeftTheMostIdleSignedInSessionGoesAsync() {
        TestClock clock = new();
        IPageSessionStore store = Store(clock, TestOptions.Stores.Visitor with { MaxLiveSessions = 2 });
        _ = await store.EnsureAsync("first", account: "owner");
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        _ = await store.EnsureAsync("second", account: "owner");
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(2);

        Assert.That(await store.EnsureAsync("third", account: "owner"), Is.True);

        Assert.That(await store.FindAsync("first"), Is.Null);
        Assert.That(await store.FindAsync("second"), Is.Not.Null);
        Assert.That(await store.FindAsync("third"), Is.Not.Null);
    }

    [Test]
    public async Task AMintIsRefusedOnlyWhenNothingCanBeEvictedAsync() {
        TestClock clock = new();
        // A cap of zero is what the configuration clamps away from; it is the one state in which the
        // table has room for nothing and holds nothing, so the guard is what answers.
        IPageSessionStore store = Store(clock, TestOptions.Stores.Visitor with { MaxLiveSessions = 0 });

        Assert.That(await store.EnsureAsync("session-id", account: null), Is.False);
        Assert.That(await store.FindAsync("session-id"), Is.Null);
    }

    [Test]
    public async Task TheSweepTrimsAnOverfullTableToItsCapAsync() {
        TestClock clock = new();
        IPageSessionStore store = Store(clock, TestOptions.Stores.Visitor with { MaxLiveSessions = 2 });
        _ = await store.EnsureAsync("first", account: null);
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        _ = await store.EnsureAsync("second", account: null);

        // Shrinking the cap is what a configuration change looks like to a table that is already full.
        IPageSessionStore smaller = Store(clock, TestOptions.Stores.Visitor with { MaxLiveSessions = 1 });
        PageSessionSweep sweep = await smaller.RemoveExpiredAsync();

        Assert.That(sweep.AnonymousEvicted, Is.EqualTo(1));
        Assert.That(await smaller.FindAsync("first"), Is.Null);
        Assert.That(await smaller.FindAsync("second"), Is.Not.Null);
    }

    [Test]
    public async Task AnEvictedSessionsValuesGoTooAsync() {
        TestClock clock = new();
        IStoreValues values = Values();
        IPageSessionStore store = Store(clock, TestOptions.Stores.Visitor with { MaxLiveSessions = 1 });
        _ = await store.EnsureAsync("first", account: null);
        await WriteSessionValue(values, "first", "key", "\"1\"");
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(1);

        _ = await store.EnsureAsync("second", account: null);

        Assert.That(await values.FindAsync([StoreSubjectKeys.ForSession("first")], "/page", "key"), Is.Null);
    }

    [Test]
    public async Task AdoptionReKeysThePersonAndOneShotAndDropsTheSessionAsync() {
        IStoreValues values = Values();
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("old", account: null);
        await Write(values, StoreSubjectKeys.ForSession("old"), "person", StoreLifetimeKind.Person);
        await Write(values, StoreSubjectKeys.ForSession("old"), "notice", StoreLifetimeKind.NextRead);
        await Write(values, StoreSubjectKeys.ForSession("old"), "draft", StoreLifetimeKind.Session);

        Assert.That(await store.AdoptAsync("old", "owner", "new"), Is.True);

        await Assert.MultipleAsync(async () => {
            Assert.That(await values.FindAsync([StoreSubjectKeys.ForAccount("owner")], "/page", "person"), Is.Not.Null);
            Assert.That(await values.FindAsync([StoreSubjectKeys.ForAccount("owner")], "/page", "notice"), Is.Not.Null);
            Assert.That(await values.FindAsync([StoreSubjectKeys.ForSession("old")], "/page", "draft"), Is.Null);
            Assert.That(await store.FindAsync("old"), Is.Null);
            Assert.That(await store.FindAsync("new"), Is.Not.Null);
        });
    }

    [Test]
    public async Task StoredTimestampsAreUtcTextAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);

        await using StorageDbContext database = new(_options);
        string stamp = await database.Database
            .SqlQuery<string>($"SELECT \"UpdatedAt\" AS \"Value\" FROM \"PageSessions\"")
            .SingleAsync();

        Assert.That(DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed),
            Is.True, stamp);
        Assert.That(parsed.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    private IStoreValues Values() => new SqliteStoreValues(
        DatabaseTemplate.Factory(_options), TestOptions.Stores, TimeProvider.System);

    private Task Write(IStoreValues values, string subject, string key, StoreLifetimeKind kind) =>
        values.WriteAsync(
            new StoreValueWrite(subject, "/page", key, (int)kind, ExpiresAt: null, "\"x\"", RequireLivePage: false),
            kind == StoreLifetimeKind.Session ? TestOptions.Stores.Visitor.SignedIn : TestOptions.Stores.Visitor.Anonymous);

    private Task WriteSessionValue(IStoreValues values, string sessionId, string key, string json) =>
        values.WriteAsync(
            new StoreValueWrite(StoreSubjectKeys.ForSession(sessionId), "/page", key, (int)StoreLifetimeKind.Session, ExpiresAt: null, json, RequireLivePage: false),
            TestOptions.Stores.Visitor.Anonymous);

    private IPageSessionStore Store(TimeProvider? clock = null, VisitorOptions? options = null) =>
        new SqlitePageSessionStore(
            DatabaseTemplate.Factory(_options),
            TestOptions.Stores with { Visitor = options ?? TestOptions.Stores.Visitor },
            TestOptions.Authentication,
            clock ?? TimeProvider.System);
}
