// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The sandbox: one statement over the subject's own rows, and nothing else.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class SqliteRecordStoreTests {

    private const string Key = "/notes";

    private static readonly PageRecordOptions Records = TestOptions.Stores.Records with {
        MaxRowsPerSubject = 3,
        MaxResultRows = 2,
    };

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() => s_template = new DatabaseTemplate("records");

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public async Task OpenDatabaseAsync() {
        _databasePath = s_template!.CreateFile("records");
        _options = DatabaseTemplate.Options(_databasePath);

        await using StorageDbContext database = new(_options);
        _ = await new DeclaredTableApplier(Records).ApplyAsync(database, PagePath.Normalize(Key), Notes());
    }

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task ARowRoundTripsThroughTheViewAndBackAgainAsync() {
        await using SqliteRecordStore store = Store("session:one");

        PageRecordResult written = await store.ExecuteAsync(
            """INSERT INTO "notes" ("Title", "Body") VALUES ($title, $body)""",
            new PageParameters().Add("title", "first").Add("body", "kept"));

        IReadOnlyList<PageRow> rows = await store.QueryAsync("""SELECT "Title", "Body" FROM "notes" """);

        Assert.Multiple(() => {
            Assert.That(written, Is.EqualTo(new PageRecordResult(0, PageRecordOutcome.Applied)),
                "a view's trigger changes the row, and SQLite counts what the statement itself changed");
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].GetString("Title"), Is.EqualTo("first"));
            Assert.That(rows[0].GetString("Body"), Is.EqualTo("kept"));
        });
    }

    [Test]
    public async Task AnotherSubjectSeesNoneOfThoseRowsAsync() {
        await using SqliteRecordStore mine = Store("session:one");
        await using SqliteRecordStore theirs = Store("session:two");
        _ = await mine.ExecuteAsync("""INSERT INTO "notes" ("Title", "Body") VALUES ('first', 'kept')""");

        Assert.That(await theirs.QueryAsync("""SELECT "Title" FROM "notes" """), Is.Empty);
    }

    [Test]
    public async Task AJoinAndAnAggregateSeeOnlyTheSubjectsRowsAsync() {
        await using SqliteRecordStore mine = Store("session:one");
        await using SqliteRecordStore theirs = Store("session:two");
        _ = await mine.ExecuteAsync("""INSERT INTO "notes" ("Title", "Body") VALUES ('mine', 'a'), ('mine too', 'b')""");
        _ = await theirs.ExecuteAsync("""INSERT INTO "notes" ("Title", "Body") VALUES ('theirs', 'c')""");

        IReadOnlyList<PageRow> rows = await mine.QueryAsync(
            """SELECT COUNT(DISTINCT note."Id") AS "n", MAX(note."Title") AS "title" FROM "notes" AS note JOIN "notes" AS twin ON twin."Id" <= note."Id" """);

        Assert.Multiple(() => {
            Assert.That(rows[0].GetInt64("n"), Is.EqualTo(2));
            Assert.That(rows[0].GetString("title"), Is.EqualTo("mine too"));
        });
    }

    // The real table cannot be named: the authorizer records the action and the name it refused.
    [Test]
    public async Task TheRealTableCannotBeNamedAsync() {
        await using SqliteRecordStore store = Store("session:one");
        string real = await RealNameAsync();

        InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.QueryAsync($"SELECT \"Title\" FROM \"{real}\"")))!;

        Assert.That(error.Message, Does.Contain("refused").And.Contain(real));
    }

    [TestCase("PRAGMA table_info(\"notes\")")]
    [TestCase("ATTACH DATABASE ':memory:' AS other")]
    [TestCase("SELECT \"name\" FROM \"sqlite_master\"")]
    [TestCase("SELECT \"name\" FROM \"sqlite_temp_master\"")]
    [TestCase("CREATE TABLE sneaky (x TEXT)")]
    public async Task EverythingOutsideTheAllowListIsRefused(string sql) {
        await using SqliteRecordStore store = Store("session:one");

        Assert.That(async () => await store.QueryAsync(sql), Throws.InvalidOperationException.And.Message.Contains("refused"));
    }

    [Test]
    public async Task AFunctionOutsideTheListIsRefusedEvenWhenItIsQuotedAsync() {
        await using SqliteRecordStore store = Store("session:one");

        Assert.That(async () => await store.QueryAsync("""SELECT "load_extension"('x')"""),
            Throws.InvalidOperationException.And.Message.Contains("refused"));
    }

    [Test]
    public async Task ASecondStatementInOneCallIsRefusedAsync() {
        await using SqliteRecordStore store = Store("session:one");

        Assert.That(async () => await store.ExecuteAsync(
            """INSERT INTO "notes" ("Title", "Body") VALUES ('first', 'a'); DELETE FROM "notes" """),
            Throws.ArgumentException.And.Message.Contains("exactly one statement"));

        Assert.That(await store.QueryAsync("""SELECT "Title" FROM "notes" """), Is.Empty, "the first statement did not run");
    }

    [Test]
    public async Task ATrailingSemicolonAndACommentAreOneStatementAsync() {
        await using SqliteRecordStore store = Store("session:one");

        Assert.That(await store.QueryAsync("""
            SELECT "Title" FROM "notes"; -- the author's own note
            """), Is.Empty);
    }

    [Test]
    public async Task AWriteThatNamesTheRowsIdIsRefusedAsync() {
        await using SqliteRecordStore store = Store("session:one");

        Assert.That(async () => await store.ExecuteAsync(
                """INSERT INTO "notes" ("Id", "Title", "Body") VALUES (1, 'first', 'a')"""),
            Throws.TypeOf<SqliteException>().And.Message.Contains("Id is the store's"));
    }

    [Test]
    public async Task TheRowCapAnswersFullRatherThanThrowingAsync() {
        await using SqliteRecordStore store = Store("session:one");
        for (int index = 0; index < Records.MaxRowsPerSubject; index++) {
            _ = await store.ExecuteAsync(
                """INSERT INTO "notes" ("Title", "Body") VALUES ('filler', 'x')""");
        }

        PageRecordResult refused = await store.ExecuteAsync(
            """INSERT INTO "notes" ("Title", "Body") VALUES ('one too many', 'x')""");

        Assert.Multiple(() => {
            Assert.That(refused.Outcome, Is.EqualTo(PageRecordOutcome.Full));
            Assert.That(refused.Affected, Is.Zero);
        });
    }

    [Test]
    public async Task AResultPastItsRowCapThrowsNamingTheSettingAsync() {
        await using SqliteRecordStore store = Store("session:one");
        _ = await store.ExecuteAsync("""INSERT INTO "notes" ("Title", "Body") VALUES ('one', 'a'), ('two', 'b'), ('three', 'c')""");

        InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.QueryAsync("""SELECT "Title" FROM "notes" """)))!;

        Assert.That(error.Message, Does.Contain("Pages:Stores:Records:MaxResultRows"));
    }

    [Test]
    public async Task ATransactionStagesWritesUntilItCommitsAsync() {
        await using SqliteRecordStore store = Store("session:one");

        await using (IPageRecordTransaction transaction = store.BeginTransaction()) {
            PageRecordResult staged = await store.ExecuteAsync(
                """INSERT INTO "notes" ("Title", "Body") VALUES ('first', 'a')""");
            await Assert.MultipleAsync(async () => {
                Assert.That(staged.Outcome, Is.EqualTo(PageRecordOutcome.Staged));
                Assert.That(await store.QueryAsync("""SELECT "Title" FROM "notes" """), Has.Count.EqualTo(1),
                    "a read inside the transaction sees what it staged");
            });

            await transaction.CommitAsync();
        }

        await using SqliteRecordStore later = Store("session:one");
        Assert.That(await later.QueryAsync("""SELECT "Title" FROM "notes" """),
            Has.Count.EqualTo(1), "the commit is what made it durable");
    }

    [Test]
    public async Task ARolledBackTransactionDiscardsWhatItStagedAsync() {
        await using SqliteRecordStore store = Store("session:one");

        await using (IPageRecordTransaction transaction = store.BeginTransaction()) {
            _ = await store.ExecuteAsync("""INSERT INTO "notes" ("Title", "Body") VALUES ('first', 'a')""");
            await transaction.RollbackAsync();
        }

        await using SqliteRecordStore later = Store("session:one");
        Assert.That(await later.QueryAsync("""SELECT "Title" FROM "notes" """), Is.Empty);
    }

    [Test]
    public async Task ASecondTransactionOnOneHandleThrowsAsync() {
        await using SqliteRecordStore store = Store("session:one");

        await using IPageRecordTransaction transaction = store.BeginTransaction();

        Assert.That(() => store.BeginTransaction(), Throws.InvalidOperationException.And.Message.Contains("already has a transaction"));
    }

    [Test]
    public async Task ATableThePageDoesNotDeclareIsNoStoreAtAllAsync() {
        await using SqliteRecordStore store = Store("session:one");

        Assert.That(async () => await store.QueryAsync("""SELECT "Title" FROM "secrets" """),
            Throws.InvalidOperationException.And.Message.Contains("refused"));
    }

    [Test]
    public async Task AHeadRenderReadsAndRefusesToWriteAsync() {
        await using SqliteRecordStore store = new(
            PagePath.Normalize(Key), _ => "session:one", SqliteRecordStore.SubjectKind.Visitor, Catalog(), Records, writable: false);

        Assert.That(async () => await store.ExecuteAsync("""INSERT INTO "notes" ("Title", "Body") VALUES ('a', 'b')"""),
            Throws.InvalidOperationException);
    }

    // Open: FTS5's own constructor reads the schema of the table it indexes, and this store refuses
    // every read of sqlite_master - which is what keeps the real table names out of a page's reach.
    // The two need reconciling before a search can be offered; SQL is unaffected.
    [Test]
    [Ignore("FTS5's constructor reads sqlite_master, which the authorizer refuses; the fence needs a decision first.")]
    public async Task ASearchAnswersTheSubjectsRowsBestMatchFirstAsync() {
        await using StorageDbContext database = new(_options);
        _ = await new DeclaredTableApplier(Records).ApplyAsync(database, PagePath.Normalize(Key), Notes(search: "Body"));

        await using SqliteRecordStore mine = Store("session:one");
        await using SqliteRecordStore theirs = Store("session:two");
        _ = await mine.ExecuteAsync("""INSERT INTO "notes" ("Title", "Body") VALUES ('a', 'the quick brown fox')""");
        _ = await theirs.ExecuteAsync("""INSERT INTO "notes" ("Title", "Body") VALUES ('b', 'the quick brown fox')""");

        IReadOnlyList<PageRow> rows = await mine.SearchAsync("notes", "quick");

        Assert.Multiple(() => {
            Assert.That(rows, Has.Count.EqualTo(1), "the index holds every subject's rows and answers one");
            Assert.That(rows[0].GetString("Title"), Is.EqualTo("a"));
        });
    }

    [Test]
    [Ignore("FTS5's constructor reads sqlite_master, which the authorizer refuses; the fence needs a decision first.")]
    public async Task ASearchOfAPageTableSeesEveryRowAsync() {
        await using StorageDbContext database = new(_options);
        _ = await new DeclaredTableApplier(Records).ApplyAsync(database, PagePath.Normalize(Key), PageNotes());

        await using SqliteRecordStore pages = new(
            PagePath.Normalize(Key), null, SqliteRecordStore.SubjectKind.Page, Catalog(), Records, writable: true);
        _ = await pages.ExecuteAsync("""INSERT INTO "pages" ("Title") VALUES ('shared')""");

        Assert.That(await pages.SearchAsync("pages", "shared"), Has.Count.EqualTo(1));
    }

    private static PageSchema Notes(string? search = null) => new([new PageTable(
        "notes",
        StoreTableSubject.Visitor,
        StoreTableLifetime.Person,
        [new PageColumn("Title", PageColumnType.Text), new PageColumn("Body", PageColumnType.Text)],
        search)]);

    private static PageSchema PageNotes() => new([new PageTable(
        "pages",
        StoreTableSubject.Page,
        StoreTableLifetime.Page,
        [new PageColumn("Title", PageColumnType.Text)],
        "Title")]);

    private SqliteRecordStore Store(string subject) => new(
        PagePath.Normalize(Key),
        _ => subject,
        SqliteRecordStore.SubjectKind.Visitor,
        Catalog(),
        Records,
        writable: true);

    private IRecordCatalog Catalog() => new SqliteRecordCatalog(DatabaseTemplate.Factory(_options));

    private async Task<string> RealNameAsync() {
        await using StorageDbContext database = new(_options);
        IReadOnlyList<DeclaredTable> tables = await DeclaredTableApplier.ListAsync(database, PagePath.Normalize(Key));

        return tables.First(table => table.Name == "notes").RealName;
    }
}
