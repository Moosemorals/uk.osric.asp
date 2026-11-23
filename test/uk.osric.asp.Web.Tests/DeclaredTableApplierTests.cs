// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The declaration is the table: a save applies the difference, inside its own transaction.</summary>
[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class DeclaredTableApplierTests {

    private const string Key = "/notes";

    private static readonly PageRecordOptions Records = TestOptions.Stores.Records;

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() => s_template = new DatabaseTemplate("declared");

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("declared");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task ASaveCreatesTheTableTheCatalogueRowTheOwnerIndexAndTheLimitTriggersAsync() {
        await ApplyAsync(Notes("Title", "Body"));

        DeclaredTable table = (await ListAsync()).Single();
        string[] names = await SchemaNamesAsync();

        Assert.Multiple(() => {
            Assert.That(table.Name, Is.EqualTo("notes"));
            Assert.That(table.RealName, Does.StartWith(DeclaredTableSql.RealNamePrefix));
            Assert.That(names, Does.Contain(table.RealName)
                .And.Contain(table.RealName + "_owner")
                .And.Contain(table.RealName + "_cap_insert")
                .And.Contain(table.RealName + "_cap_update"));
        });
    }

    [Test]
    public async Task AnAddedColumnIsAlteredOntoTheTableAndTheRowsStayAsync() {
        await ApplyAsync(Notes("Title"));
        Assert.That(await InsertAsync("kept"), Is.EqualTo(1));

        await ApplyAsync(Notes("Title", "Body"));

        await Assert.MultipleAsync(async () => {
            Assert.That(await ColumnsAsync(), Is.EqualTo(ColumnNames));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM " + await QuotedTableAsync()), Is.EqualTo(1L));
        });
    }

    [Test]
    public async Task AColumnTheSourceNoLongerNamesIsDroppedAndTheRowsStayAsync() {
        await ApplyAsync(Notes("Title", "Body"));
        _ = await InsertAsync("kept", body: "gone");

        await ApplyAsync(Notes("Title"));

        await Assert.MultipleAsync(async () => {
            Assert.That(await ColumnsAsync(), Is.EqualTo(OwnedColumns));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM " + await QuotedTableAsync()), Is.EqualTo(1L),
                "the row stays; only the column that went takes its values");
        });
    }

    [Test]
    public async Task ATableTheSourceNoLongerNamesIsDroppedWithEveryRowInItAsync() {
        await ApplyAsync(Notes("Title"));
        string real = (await ListAsync()).Single().RealName;
        _ = await InsertAsync("kept");

        await ApplyAsync(PageSchema.Empty);

        await Assert.MultipleAsync(async () => {
            Assert.That(await SchemaNamesAsync(), Does.Not.Contain(real));
            Assert.That(await ListAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task AChangedColumnTypeIsRefusedAndTheTableIsLeftAloneAsync() {
        await ApplyAsync(Notes("Title"));
        PageSchema changed = new([new PageTable("notes", StoreTableSubject.Visitor, StoreTableLifetime.Person,
            [new PageColumn("Title", PageColumnType.WholeNumber)])]);

        IReadOnlyList<PageDiagnostic> problems = await ApplyAsync(changed);

        await Assert.MultipleAsync(async () => {
            Assert.That(Messages(problems), Has.Some.Contains("PAGE012").And.Contains("keeps the type it was made with"));
            Assert.That(await ColumnsAsync(), Is.EqualTo(OwnedColumns), "a refused declaration applies nothing");
        });
    }

    [Test]
    public async Task AChangedSubjectOrLifetimeIsRefusedAsync() {
        await ApplyAsync(Notes("Title"));
        PageSchema changed = new([new PageTable("notes", StoreTableSubject.Page, StoreTableLifetime.Page,
            [new PageColumn("Title", PageColumnType.Text)])]);

        Assert.That(Messages(await ApplyAsync(changed)), Has.Some.Contains("keeps the subject and lifetime"));
    }

    [Test]
    public async Task ADeclarationOutsideItsLimitIsRefusedBeforeAnyDdlRunsAsync() {
        PageTable[] tooMany = [.. Enumerable.Range(0, Records.MaxTablesPerSource + 1)
            .Select(index => new PageTable($"t{index}", StoreTableSubject.Page, StoreTableLifetime.Page,
                [new PageColumn("A", PageColumnType.Text)]))];

        IReadOnlyList<PageDiagnostic> problems = await ApplyAsync(new PageSchema(tooMany));

        await Assert.MultipleAsync(async () => {
            Assert.That(problems, Is.Not.Empty);
            Assert.That(await ListAsync(), Is.Empty, "nothing was made");
        });
    }

    [Test]
    public async Task ASearchColumnGetsAnIndexAndTheThreeTriggersThatKeepItAsync() {
        await ApplyAsync(new PageSchema([NotesTable("Title") with { Search = "Title" }]));

        string real = (await ListAsync()).Single().RealName;

        Assert.That(await SchemaNamesAsync(), Does.Contain(DeclaredTableSql.SearchName(real))
            .And.Contain(real + "_ai")
            .And.Contain(real + "_ad")
            .And.Contain(real + "_au"));
    }

    [Test]
    public async Task MovingTheSearchColumnRebuildsTheIndexOnTheNewColumnAsync() {
        await ApplyAsync(new PageSchema([NotesTable("Title", "Body") with { Search = "Title" }]));
        string real = (await ListAsync()).Single().RealName;

        await ApplyAsync(new PageSchema([NotesTable("Title", "Body") with { Search = "Body" }]));

        await Assert.MultipleAsync(async () => {
            Assert.That(await SchemaNamesAsync(), Does.Contain(DeclaredTableSql.SearchName(real)).And.Contain(real + "_ai"));
            Assert.That((await ListAsync()).Single().Search, Is.EqualTo("Body"));
        });
    }

    [Test]
    public async Task TheRowLimitRefusesTheWriteInsideTheStatementAsync() {
        DeclaredTableApplier applier = new(Records with { MaxRowsPerSubject = 1 });

        await ApplyAsync(Notes("Title"), applier);
        _ = await InsertAsync("first");

        SqliteException error = (await Assert.ThrowsAsync<SqliteException>(() => InsertAsync("second")))!;

        Assert.That(error.Message, Does.Contain("FULL"));
    }

    // The save is where the declaration is applied, so the store is proved to do it rather than the
    // applier on its own: a page saved with a declaration has its table, and one saved without an
    // image — a source that does not compile — makes nothing at all.
    [Test]
    public async Task ASaveAppliesTheDeclarationTheRevisionCarriesAsync() {
        await using (StorageDbContext database = new(_options)) {
            SqlitePageStore saving = new(database, NullLogger<SqlitePageStore>.Instance, declarations: Applier());
            await saving.SavePageAsync(Template(Notes("Title").Tables));
        }

        Assert.That((await ListAsync()).Single().Name, Is.EqualTo("notes"));
    }

    // A library carries a declaration and no image, and does apply it; a revision that failed to
    // compile carries a declaration too, and applies nothing at all.
    [Test]
    public async Task ARevisionThatDoesNotCompileAppliesNoDdlAsync() {
        PageDiagnostic broken = new(PageDiagnosticSeverity.Error, null, "PAGE002: a page must render something.");

        await using (StorageDbContext database = new(_options)) {
            SqlitePageStore saving = new(database, NullLogger<SqlitePageStore>.Instance, declarations: Applier());
            await saving.SavePageAsync(new PageTemplate(
                PagePath.Normalize(Key), "<p>broken</p>",
                new PageImage("hash", "runtime", null, null, [broken], Schema: Notes("Title"))));
        }

        Assert.That(await ListAsync(), Is.Empty, "a revision that does not compile applies nothing");
    }

    [Test]
    public async Task ALibraryWithNoImageAppliesItsDeclarationAsync() {
        await using (StorageDbContext database = new(_options)) {
            SqlitePageStore saving = new(database, NullLogger<SqlitePageStore>.Instance, declarations: Applier());
            await saving.SavePageAsync(new PageTemplate(
                PagePath.Normalize(Key), "<%! [PageTable(\"notes\")] public sealed class Note { public long N { get; set; } } %>",
                new PageImage("hash", "runtime", null, null, [], Schema: Notes("Title"), Libraries: null)));
        }

        Assert.That((await ListAsync()).Single().Name, Is.EqualTo("notes"),
            "a library owns its tables, so its save is what applies them");
    }

    [Test]
    public async Task ARefusedDeclarationKeepsNoImageAndStoresItsDiagnosticAsync() {
        PageTable[] tooMany = [.. Enumerable.Range(0, Records.MaxTablesPerSource + 1)
            .Select(index => new PageTable($"t{index}", StoreTableSubject.Page, StoreTableLifetime.Page,
                [new PageColumn("A", PageColumnType.Text)]))];

        await using (StorageDbContext database = new(_options)) {
            SqlitePageStore saving = new(database, NullLogger<SqlitePageStore>.Instance, declarations: Applier());
            await saving.SavePageAsync(new PageTemplate(
                PagePath.Normalize(Key), "<p>hi</p>", new PageImage("hash", "runtime", [1], [2], [], Schema: new PageSchema(tooMany))));
        }

        await using (StorageDbContext database = new(_options)) {
            PageRevision row = await database.Set<PageRevision>().AsNoTracking().SingleAsync(revision => revision.Path == Key);
            Assert.Multiple(() => {
                Assert.That(row.Image, Is.Null, "the page answers its diagnostic rather than running without its table");
                Assert.That(row.Diagnostics, Does.Contain("PAGE012"));
            });
        }

        Assert.That(await ListAsync(), Is.Empty, "and the schema is left alone");
    }

    [Test]
    public async Task DroppingASourceRemovesEveryTableItDeclaredAsync() {
        await ApplyAsync(Notes("Title"));
        string real = (await ListAsync()).Single().RealName;

        await using (StorageDbContext database = new(_options)) {
            await DeclaredTableApplier.DropAsync(database, PagePath.Normalize(Key));
        }

        await Assert.MultipleAsync(async () => {
            Assert.That(await SchemaNamesAsync(), Does.Not.Contain(real));
            Assert.That(await ListAsync(), Is.Empty);
        });
    }

    private static readonly string[] OwnedColumns = ["Id", "Owner", "Title"];
    private static readonly string[] ColumnNames = ["Id", "Owner", "Title", "Body"];

    private DeclaredTableApplier Applier() => new(Records);

    private PageTemplate Template(IReadOnlyList<PageTable> tables) => new(
        PagePath.Normalize(Key), "<p>hi</p>", new PageImage("hash", "runtime", [1], [2], [], Schema: new PageSchema(tables)));

    private static PageSchema Notes(params string[] columns) => new([NotesTable(columns)]);

    private static PageTable NotesTable(params string[] columns) => new(
        "notes",
        StoreTableSubject.Visitor,
        StoreTableLifetime.Person,
        [.. columns.Select(column => new PageColumn(column, PageColumnType.Text))]);

    private async Task<IReadOnlyList<PageDiagnostic>> ApplyAsync(PageSchema schema, DeclaredTableApplier? applier = null) {
        await using StorageDbContext database = new(_options);

        return await (applier ?? new DeclaredTableApplier(Records))
            .ApplyAsync(database, PagePath.Normalize(Key), schema);
    }

    private async Task<IReadOnlyList<DeclaredTable>> ListAsync() {
        await using StorageDbContext database = new(_options);

        return await DeclaredTableApplier.ListAsync(database, PagePath.Normalize(Key));
    }

    private static string[] Messages(IReadOnlyList<PageDiagnostic> problems) =>
        [.. problems.Select(problem => problem.Message)];

    private async Task<string> QuotedTableAsync() => $"\"{(await ListAsync()).Single().RealName}\"";

    private async Task<string[]> ColumnsAsync() {
        await using SqliteConnection connection = new(DatabaseTemplate.ConnectionString(_databasePath));
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{(await ListAsync()).Single().RealName}\")";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();

        List<string> columns = [];
        while (await reader.ReadAsync()) {
            columns.Add(reader.GetString(1));
        }

        return [.. columns];
    }

    private async Task<long> InsertAsync(string title, string? body = null) {
        await using SqliteConnection connection = new(DatabaseTemplate.ConnectionString(_databasePath));
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        string columns = body is null ? "\"Owner\", \"Title\"" : "\"Owner\", \"Title\", \"Body\"";
        string values = body is null ? "'session:one', $title" : "'session:one', $title, $body";
        command.CommandText = $"INSERT INTO \"{(await ListAsync()).Single().RealName}\" ({columns}) VALUES ({values})";
        command.Parameters.AddWithValue("$title", title);
        if (body is not null) {
            command.Parameters.AddWithValue("$body", body);
        }

        return await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql) {
        await using SqliteConnection connection = new(DatabaseTemplate.ConnectionString(_databasePath));
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string[]> SchemaNamesAsync() {
        await using SqliteConnection connection = new(DatabaseTemplate.ConnectionString(_databasePath));
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT \"name\" FROM sqlite_master WHERE \"type\" IN ('table', 'trigger', 'index')";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();

        List<string> names = [];
        while (await reader.ReadAsync()) {
            names.Add(reader.GetString(0));
        }

        return [.. names];
    }
}
