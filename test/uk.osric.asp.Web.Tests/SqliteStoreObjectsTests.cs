// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class SqliteStoreObjectsTests {

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;
    private TestClock _clock = new();

    [OneTimeSetUp]
    public static void MigrateOnce() => s_template = new DatabaseTemplate("objects");

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("objects");
        _options = DatabaseTemplate.Options(_databasePath);
        _clock = new TestClock();
    }

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task BytesRoundTripThroughTheStreamAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("the bytes of the object, read a little at a time");
        _ = await WriteAsync(store, "page:/a", content, "application/octet-stream");

        await using PageObjectStream stream = (await store.OpenPageAsync(PageObjectHash.Of(content)))!;
        byte[] read = await ReadAllAsync(stream);

        Assert.Multiple(() => {
            Assert.That(read, Is.EqualTo(content));
            Assert.That(stream.Length, Is.EqualTo(content.Length));
            Assert.That(stream.ContentType, Is.EqualTo("application/octet-stream"));
        });
    }

    [Test]
    public async Task ASliceStartsWhereTheRangeSaysAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("0123456789");
        _ = await WriteAsync(store, "page:/a", content);
        string sha = PageObjectHash.Of(content);

        await using IServedObject stream = (await store.OpenServedAsync(sha, []))!;
        byte[] slice = new byte[4];
        int read = await stream.ReadAtAsync(slice, 3, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(read, Is.EqualTo(4));
            Assert.That(Encoding.UTF8.GetString(slice), Is.EqualTo("3456"));
        });
    }

    [Test]
    public async Task AnEmptyObjectRoundTripsAsync() {
        IStoreObjects store = Objects();
        byte[] content = [];
        _ = await WriteAsync(store, "page:/empty", content);

        await using PageObjectStream stream = (await store.OpenPageAsync(PageObjectHash.Of(content)))!;
        byte[] read = await ReadAllAsync(stream);

        Assert.Multiple(() => {
            Assert.That(stream.Length, Is.Zero);
            Assert.That(read, Is.Empty);
        });
    }

    [Test]
    public async Task TheSameBytesUnderOneSubjectAreOneRowAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("dedupe me");
        _ = await WriteAsync(store, "page:/a", content, lifetime: StoreLifetimeKind.Page);
        _ = await WriteAsync(store, "page:/a", content, lifetime: StoreLifetimeKind.Page);

        await using StorageDbContext database = new(_options);
        Assert.That(await database.Set<StoreObjectEntry>().CountAsync(row => row.Subject == "page:/a"), Is.EqualTo(1));
    }

    [Test]
    public async Task AnotherSubjectCannotOpenAVisitorObjectAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("mine");
        _ = await WriteAsync(store, "session:one", content, lifetime: StoreLifetimeKind.Person);
        string sha = PageObjectHash.Of(content);

        Assert.That(await store.OpenAsync(Subjects("session:two"), sha), Is.Null);
        Assert.That(await store.OpenAsync(Subjects("session:one"), sha), Is.Not.Null);
    }

    [Test]
    public async Task APageObjectIsOpenToAnyPageAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("public");
        _ = await WriteAsync(store, "page:/written-by", content);

        await using PageObjectStream? stream = await store.OpenPageAsync(PageObjectHash.Of(content));

        Assert.That(stream, Is.Not.Null);
    }

    [Test]
    public async Task AnExpiredObjectIsNotOpenedAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("gone");
        _ = await WriteAsync(
            store,
            "page:/a",
            content,
            expiresAt: StoreSubjectKeys.Stamp(_clock.GetUtcNow().AddSeconds(-1)));

        Assert.That(await store.OpenPageAsync(PageObjectHash.Of(content)), Is.Null);
    }

    [Test]
    public async Task TheSubjectByteLimitRefusesAWriteAsync() {
        IStoreObjects store = Objects();
        PageObjectOptions limits = TestOptions.Stores.Objects with { MaxSubjectBytes = 4 };

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await WriteAsync(store, "page:/a", Encoding.UTF8.GetBytes("12345"), limits: limits)))!;

        Assert.That(refused.Message, Does.Contain("may use 4"));
    }

    [Test]
    public async Task TheStoreRefusesAnObjectOverItsOwnLimitAsync() {
        IStoreObjects store = Objects();
        PageObjectOptions limits = TestOptions.Stores.Objects with { MaxObjectBytes = 4 };

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await WriteAsync(store, "page:/a", Encoding.UTF8.GetBytes("12345"), limits: limits)))!;

        Assert.That(refused.Message, Does.Contain("MaxObjectBytes"));
    }

    [Test]
    public async Task OneObjectOverItsOwnLimitIsRefusedByTheCapabilityAsync() {
        IStoreObjects store = Objects();
        PageObjectOptions limits = TestOptions.Stores.Objects with { MaxObjectBytes = 4 };
        PageObjectStore objects = new(
            PagePath.Normalize("/a"), store, limits, writable: true);

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await objects.PutAsync(Encoding.UTF8.GetBytes("12345"), "application/octet-stream")))!;

        Assert.That(refused.Message, Does.Contain("Pages:Stores:Objects:MaxObjectBytes"));
    }

    [Test]
    public async Task MovingAPageReKeysItsObjectsAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("move me");
        _ = await WriteAsync(store, "page:/from", content);

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.MovePageAsync(
                database, PagePath.Normalize("/from"), PagePath.Normalize("/to"), CancellationToken.None);
        }

        await using StorageDbContext check = new(_options);
        Assert.Multiple(() => {
            Assert.That(check.Set<StoreObjectEntry>().Any(row => row.Subject == "page:/to"), Is.True);
            Assert.That(check.Set<StoreObjectEntry>().Any(row => row.Subject == "page:/from"), Is.False);
        });
    }

    [Test]
    public async Task AdoptionReKeysAPersonObjectAndLeavesASessionOneAsync() {
        IStoreObjects store = Objects();
        byte[] person = Encoding.UTF8.GetBytes("follow me");
        byte[] session = Encoding.UTF8.GetBytes("stay behind");
        _ = await WriteAsync(store, "session:s1", person, lifetime: StoreLifetimeKind.Person);
        _ = await WriteAsync(store, "session:s1", session, lifetime: StoreLifetimeKind.Session);

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.AdoptAsync(database, "s1", "owner", CancellationToken.None);
        }

        await using StorageDbContext check = new(_options);
        Assert.Multiple(() => {
            Assert.That(
                check.Set<StoreObjectEntry>().Any(row => row.Subject == "account:owner" && row.Sha == PageObjectHash.Of(person)),
                Is.True);
            Assert.That(
                check.Set<StoreObjectEntry>().Any(row => row.Subject == "session:s1" && row.Sha == PageObjectHash.Of(session)),
                Is.True);
        });
    }

    [Test]
    public async Task AdoptionKeepsOneRowWhenTheAccountAlreadyHasTheSameBytesAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("already mine");
        _ = await WriteAsync(store, "session:s1", content, lifetime: StoreLifetimeKind.Person);
        _ = await WriteAsync(store, "account:owner", content, lifetime: StoreLifetimeKind.Person);

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.AdoptAsync(database, "s1", "owner", CancellationToken.None);
        }

        await using StorageDbContext check = new(_options);
        Assert.Multiple(() => {
            Assert.That(
                check.Set<StoreObjectEntry>().Count(row => row.Subject == "account:owner" && row.Sha == PageObjectHash.Of(content)),
                Is.EqualTo(1));
            Assert.That(check.Set<StoreObjectEntry>().Any(row => row.Subject == "session:s1"), Is.False);
        });
    }

    [Test]
    public async Task SigningOutRemovesTheSessionsObjectsAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("gone");
        _ = await WriteAsync(store, "session:s1", content, lifetime: StoreLifetimeKind.Session);

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.DeleteSubjectAsync(database, "session:s1", CancellationToken.None);
        }

        Assert.That(await store.OpenAsync(Subjects("session:s1"), PageObjectHash.Of(content)), Is.Null);
    }

    [Test]
    public async Task TheSweepRemovesAnExpiredObjectAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("old");
        _ = await WriteAsync(
            store,
            "page:/a",
            content,
            expiresAt: StoreSubjectKeys.Stamp(_clock.GetUtcNow().AddSeconds(-1)));

        int removed = await store.RemoveExpiredAsync(TimeSpan.FromDays(365));

        Assert.That(removed, Is.EqualTo(1));
    }

    [Test]
    public async Task ServingPrefersAPageObjectOverAVisitorsAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("same bytes");
        _ = await WriteAsync(store, "session:s1", content, lifetime: StoreLifetimeKind.Person);
        _ = await WriteAsync(store, "page:/a", content);

        await using IServedObject served = (await store.OpenServedAsync(
            PageObjectHash.Of(content), Subjects("session:s1")))!;

        Assert.That(served.Public, Is.True);
    }

    [Test]
    public async Task RemovingAPageTakesItsObjectsAsync() {
        IStoreObjects store = Objects();
        byte[] content = Encoding.UTF8.GetBytes("page bytes");
        _ = await WriteAsync(store, "page:/doomed", content);

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.DeletePageAsync(database, PagePath.Normalize("/doomed"), CancellationToken.None);
        }

        Assert.That(await store.OpenPageAsync(PageObjectHash.Of(content)), Is.Null);
    }

    private IStoreObjects Objects() => new SqliteStoreObjects(
        DatabaseTemplate.Factory(_options),
        TestOptions.Stores,
        _clock);

    private async Task<PageObjectRef> WriteAsync(
        IStoreObjects store,
        string subject,
        byte[] content,
        string contentType = "application/octet-stream",
        StoreLifetimeKind lifetime = StoreLifetimeKind.Page,
        string? expiresAt = null,
        PageObjectOptions? limits = null) {
        string sha = PageObjectHash.Of(content);
        await store.WriteAsync(
            new StoreObjectWrite(
                subject,
                sha,
                content.LongLength,
                contentType,
                (int)lifetime,
                expiresAt,
                content,
                RequireLivePage: false),
            limits ?? TestOptions.Stores.Objects);

        return new PageObjectRef(sha, content.LongLength, contentType);
    }

    private static async Task<byte[]> ReadAllAsync(PageObjectStream stream) {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[3];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), CancellationToken.None)) > 0) {
            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }

        return buffer.ToArray();
    }

    private static IReadOnlyList<string> Subjects(params string[] subjects) => subjects;
}
