// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using System.Globalization;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
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
    public async Task AValueSurvivesReopeningTheDatabaseAsync() {
        IPageSessionStore store = Store();
        Assert.That(await store.EnsureAsync("session-id", account: null), Is.True);

        await store.SetValueAsync("session-id", "/page", "draft", "\"hello\"", PageSessionCaps.Anonymous);

        Assert.That(await Store().GetValueAsync("session-id", "/page", "draft"), Is.EqualTo("\"hello\""));
    }

    [Test]
    public async Task AMissingKeyReadsAsNullAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);

        Assert.That(await store.GetValueAsync("session-id", "/page", "absent"), Is.Null);
    }

    [Test]
    public async Task SessionsAreIsolatedAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("first", account: null);
        _ = await store.EnsureAsync("second", account: null);
        await store.SetValueAsync("first", "/page", "shared", "1", PageSessionCaps.Anonymous);

        Assert.That(await store.GetValueAsync("second", "/page", "shared"), Is.Null);
        Assert.That(await store.ListKeysAsync("second", "/page"), Is.Empty);
    }

    [Test]
    public async Task PagesAreIsolatedWithinOneSessionAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);
        await store.SetValueAsync("session-id", "/first", "draft", "1", PageSessionCaps.Anonymous);

        Assert.That(await store.GetValueAsync("session-id", "/second", "draft"), Is.Null);
        Assert.That(await store.ListKeysAsync("session-id", "/first"), Is.EqualTo(new List<string> { "draft" }));
    }

    [Test]
    public async Task KeysComeBackInOrdinalOrderAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);
        await store.SetValueAsync("session-id", "/page", "b", "1", PageSessionCaps.Anonymous);
        await store.SetValueAsync("session-id", "/page", "A", "1", PageSessionCaps.Anonymous);
        await store.SetValueAsync("session-id", "/page", "a", "1", PageSessionCaps.Anonymous);

        Assert.That(await store.ListKeysAsync("session-id", "/page"), Is.EqualTo(new List<string> { "A", "a", "b" }));
    }

    [Test]
    public async Task AllKeysAreOrderedByPageAndThenKeyAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);
        _ = await store.EnsureAsync("other-session", account: null);
        await store.SetValueAsync("session-id", "/b", "two", "1", PageSessionCaps.Anonymous);
        await store.SetValueAsync("session-id", "/a", "second", "1", PageSessionCaps.Anonymous);
        await store.SetValueAsync("session-id", "/a", "first", "1", PageSessionCaps.Anonymous);
        await store.SetValueAsync("other-session", "/a", "apart", "1", PageSessionCaps.Anonymous);

        Assert.That(await store.ListAllKeysAsync("session-id"), Is.EqualTo(new List<uk.osric.asp.Sdk.PageSessionKey> {
            new("/a", "first"),
            new("/a", "second"),
            new("/b", "two"),
        }));
    }

    [Test]
    public async Task DeleteReportsWhetherTheKeyExistedAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);
        await store.SetValueAsync("session-id", "/page", "key", "1", PageSessionCaps.Anonymous);

        Assert.That(await store.DeleteValueAsync("session-id", "/page", "key"), Is.True);
        Assert.That(await store.DeleteValueAsync("session-id", "/page", "key"), Is.False);
    }

    [Test]
    public async Task DeletingASessionTakesItsValuesAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);
        await store.SetValueAsync("session-id", "/page", "key", "1", PageSessionCaps.Anonymous);

        Assert.That(await store.DeleteAsync("session-id"), Is.True);

        await using StorageDbContext database = new(_options);
        Assert.That(await database.Set<PageSessionValueEntry>().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task AWriteToAMissingSessionIsRefusedAsync() {
        IPageSessionStore store = Store();

        ArgumentException? refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SetValueAsync("gone", "/page", "key", "1", PageSessionCaps.Anonymous));

        Assert.That(refused?.Message, Does.Contain("no longer live"));
    }

    [Test]
    public async Task TheKeyCountCapRefusesTheWriteAndLeavesNoRowAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);
        PageSessionCaps caps = new(MaxValueBytes: 1024, MaxKeys: 2, MaxTotalBytes: 4096);
        await store.SetValueAsync("session-id", "/page", "one", "1", caps);
        await store.SetValueAsync("session-id", "/page", "two", "2", caps);

        ArgumentException? refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SetValueAsync("session-id", "/page", "three", "3", caps));

        Assert.That(refused?.Message, Does.Contain("2 keys"));
        Assert.That(await store.ListKeysAsync("session-id", "/page"), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task TheByteCapRefusesTheWriteAndLeavesTheOldValueAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);
        PageSessionCaps caps = new(MaxValueBytes: 4096, MaxKeys: 8, MaxTotalBytes: 64);
        await store.SetValueAsync("session-id", "/page", "small", "1", caps);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SetValueAsync("session-id", "/page", "big", new string('x', 200), caps));

        Assert.That(await store.GetValueAsync("session-id", "/page", "small"), Is.EqualTo("1"));
        Assert.That(await store.GetValueAsync("session-id", "/page", "big"), Is.Null);
    }

    [Test]
    public async Task AnUpdateIsAllowedWhenTheSessionIsAlreadyFullAsync() {
        IPageSessionStore store = Store();
        _ = await store.EnsureAsync("session-id", account: null);
        PageSessionCaps caps = new(MaxValueBytes: 4096, MaxKeys: 8, MaxTotalBytes: 64);
        await store.SetValueAsync("session-id", "/page", "key", new string('x', 40), caps);

        await store.SetValueAsync("session-id", "/page", "key", new string('y', 40), caps);

        Assert.That(await store.GetValueAsync("session-id", "/page", "key"), Is.EqualTo(new string('y', 40)));
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
        Assert.That(await store.FindAsync("signed-in"), Is.Not.Null, "a signed-in session has eight hours");
    }

    [Test]
    public async Task ASignedInSessionOutlivesTheAnonymousWindowAsync() {
        TestClock clock = new();
        IPageSessionStore store = Store(clock);
        _ = await store.EnsureAsync("signed-in", account: "owner");
        clock.Now = DateTimeOffset.UnixEpoch.AddHours(7);

        PageSessionSweep sweep = await store.RemoveExpiredAsync();

        Assert.That(sweep.Expired, Is.Zero);
        Assert.That(await store.FindAsync("signed-in"), Is.Not.Null);
    }

    [Test]
    public async Task AMintRemovesExpiredRowsBeforeItEvictsALiveSessionAsync() {
        TestClock clock = new();
        PageSessionOptions options = new() { MaxLiveSessions = 2 };
        IPageSessionStore store = Store(clock, options);
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
        IPageSessionStore store = Store(clock, new PageSessionOptions { MaxLiveSessions = 2 });
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
        IPageSessionStore store = Store(clock, new PageSessionOptions { MaxLiveSessions = 2 });
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
        IPageSessionStore store = Store(clock, new PageSessionOptions { MaxLiveSessions = 0 });

        Assert.That(await store.EnsureAsync("session-id", account: null), Is.False);
        Assert.That(await store.FindAsync("session-id"), Is.Null);
    }

    [Test]
    public async Task TheSweepTrimsAnOverfullTableToItsCapAsync() {
        TestClock clock = new();
        PageSessionOptions options = new() { MaxLiveSessions = 2 };
        IPageSessionStore store = Store(clock, options);
        _ = await store.EnsureAsync("first", account: null);
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        _ = await store.EnsureAsync("second", account: null);

        // Shrinking the cap is what a configuration change looks like to a table that is already full.
        IPageSessionStore smaller = Store(clock, new PageSessionOptions { MaxLiveSessions = 1 });
        PageSessionSweep sweep = await smaller.RemoveExpiredAsync();

        Assert.That(sweep.AnonymousEvicted, Is.EqualTo(1));
        Assert.That(await smaller.FindAsync("first"), Is.Null);
        Assert.That(await smaller.FindAsync("second"), Is.Not.Null);
    }

    [Test]
    public async Task AnEvictedSessionsValuesGoTooAsync() {
        TestClock clock = new();
        PageSessionOptions options = new() { MaxLiveSessions = 1 };
        IPageSessionStore store = Store(clock, options);
        _ = await store.EnsureAsync("first", account: null);
        await store.SetValueAsync("first", "/page", "key", "1", PageSessionCaps.Anonymous);
        clock.Now = DateTimeOffset.UnixEpoch.AddMinutes(1);

        _ = await store.EnsureAsync("second", account: null);

        await using StorageDbContext database = new(_options);
        Assert.That(await database.Set<PageSessionValueEntry>().CountAsync(), Is.Zero);
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

    private IPageSessionStore Store(TimeProvider? clock = null, PageSessionOptions? options = null) =>
        new SqlitePageSessionStore(
            DatabaseTemplate.Factory(_options),
            options ?? new PageSessionOptions(),
            clock ?? TimeProvider.System);
}
