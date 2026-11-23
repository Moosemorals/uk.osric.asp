// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;
using PageKind = uk.osric.asp.Core.Pages.PageKind;

/// <summary>The one escape from a layer's own keys: a live library's or worker's namespace, and nothing else.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class PageStoresScopeTests {

    private const string CallerKey = "/caller";
    private const string LibraryKey = "/lib";

    private static readonly PageRecordOptions Records = TestOptions.Stores.Records;

    private static DatabaseTemplate? s_template;

    private readonly FakeStoreObjects _objects = new();
    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() => s_template = new DatabaseTemplate("stores-scope");

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public async Task ApplyTheLibraryDeclarationAsync() {
        _databasePath = s_template!.CreateFile("stores-scope");
        _options = DatabaseTemplate.Options(_databasePath);

        await using StorageDbContext database = new(_options);
        Assert.That(
            await new DeclaredTableApplier(Records).ApplyAsync(database, PagePath.Normalize(LibraryKey), Notes()),
            Is.Empty);
    }

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task AForOfALiveLibraryReachesItsTablesAsync() {
        FakePageReadStore readStore = new();
        readStore.Save(LibraryKey, "a library", templateKind: PageKind.Library);
        await InsertTitleAsync("hello");

        IReadOnlyList<PageRow> rows = await Scope(readStore).For(LibraryKey).Records
            .QueryAsync("""SELECT "Title" FROM "notes" """);

        Assert.That(rows.Single().GetString("Title"), Is.EqualTo("hello"));
    }

    [Test]
    public async Task AForOfALiveWorkerReachesItsStoreAsync() {
        FakePageReadStore readStore = new();
        readStore.Save("/worker", "a worker", templateKind: PageKind.Worker);

        IReadOnlyList<PageRow> rows = await Scope(readStore).For("/worker").Records
            .QueryAsync("SELECT 1 AS one");

        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AForOfALiveLibraryReachesItsObjectsAsync() {
        FakePageReadStore readStore = new();
        readStore.Save(LibraryKey, "a library", templateKind: PageKind.Library);
        PageStoresScope scope = Scope(readStore);

        PageObjectRef reference = await scope.For(LibraryKey).Objects.PutAsync([1, 2, 3], "application/octet-stream");

        Assert.That(await scope.Page.Objects.ExistsAsync(reference.Sha), Is.True);
    }

    [Test]
    public async Task AForOfAPageKeyIsRefusedByObjectsAsync() {
        FakePageReadStore readStore = new();
        readStore.Save("/plain", "a page");
        IPageSubject other = Scope(readStore).For("/plain");

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await other.Objects.PutAsync([1], "application/octet-stream")))!;

        Assert.That(refused.Message, Does.Contain("not a live library or worker"));
    }

    [Test]
    public async Task AForOfAPageKeyIsRefusedByValuesAsync() {
        FakePageReadStore readStore = new();
        readStore.Save("/plain", "a page");
        IPageSubject other = Scope(readStore).For("/plain");

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await other.Values.KeysAsync()))!;

        Assert.That(refused.Message, Does.Contain("not a live library or worker"));
    }

    [Test]
    public async Task AForOfAPageKeyIsRefusedByRecordsAsync() {
        FakePageReadStore readStore = new();
        readStore.Save("/plain", "a page");
        IPageSubject other = Scope(readStore).For("/plain");

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await other.Records.QueryAsync("SELECT 1 AS one")))!;

        Assert.That(refused.Message, Does.Contain("not a live library or worker"));
    }

    [Test]
    public async Task AForOfADeletedLibraryIsRefusedByRecordsAsync() {
        FakePageReadStore readStore = new();
        readStore.Save(LibraryKey, "a library", templateKind: PageKind.Library);
        readStore.Delete(LibraryKey);
        IPageSubject other = Scope(readStore).For(LibraryKey);

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await other.Records.QueryAsync("SELECT 1 AS one")))!;

        Assert.That(refused.Message, Does.Contain("not a live library or worker"));
    }

    [Test]
    public async Task AForOfAReservedKeyIsRefusedByRecordsAsync() {
        FakePageReadStore readStore = new();
        IPageSubject other = Scope(readStore).For("/edit");

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await other.Records.QueryAsync("SELECT 1 AS one")))!;

        Assert.That(refused.Message, Does.Contain("not a live library or worker"));
    }

    private PageStoresScope Scope(FakePageReadStore readStore) => new(
        PagePath.Normalize(CallerKey),
        new PageReaderSubject(
            new PageSessionRequest(PageSessionCookie.Mint(), null, presented: false, exists: false),
            TestOptions.Stores.Visitor,
            browserId: null),
        new FakeStoreValues(),
        new FakeStoreMessages(),
        new ReservedPaths(ReservedPaths.Default),
        new FakePageSessionStore(),
        TestOptions.Stores,
        readStore,
        writable: true,
        commit: new PageCommitState(),
        records: new SqliteRecordCatalog(DatabaseTemplate.Factory(_options)),
        objects: _objects);

    private async Task InsertTitleAsync(string title) {
        await using SqliteRecordStore library = new(
            PagePath.Normalize(LibraryKey),
            subject: null,
            SqliteRecordStore.SubjectKind.Page,
            new SqliteRecordCatalog(DatabaseTemplate.Factory(_options)),
            Records,
            writable: true);

        _ = await library.ExecuteAsync(
            """INSERT INTO "notes" ("Title") VALUES ($title)""",
            new PageParameters().Add("title", title));
    }

    private static PageSchema Notes() => new([
        new PageTable(
            "notes",
            StoreTableSubject.Page,
            StoreTableLifetime.Page,
            [new PageColumn("Title", PageColumnType.Text)]),
    ]);
}
