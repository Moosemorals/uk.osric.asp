// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using System.Text;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqlitePageStoreTests {
    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("storage");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("storage");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task MissingPageReturnsNullAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);

        PageTemplate? page = await storage.GetPageAsync(Key("/missing.cs"));

        Assert.That(page, Is.Null);
    }

    [TestCase("")]
    [TestCase("public class Example {}")]
    [TestCase("line one\nline two: \u00e9")]
    public async Task WrittenTemplateSurvivesReopeningAsync(string template) {
        await using (StorageDbContext writer = new(_options)) {
            IPageStore storage = new SqlitePageStore(writer);
            await storage.SavePageAsync(new PageTemplate(Key("/example.cs"), template));
        }

        await using StorageDbContext reader = new(_options);
        IPageStore reopened = new SqlitePageStore(reader);
        PageTemplate? page = await reopened.GetPageAsync(Key("/example.cs"));

        Assert.That(page?.Template, Is.EqualTo(template));
        Assert.That(page?.Image, Is.Null);
    }

    [Test]
    public async Task SavesKeepDistinctRevisionsAndAdvanceTheHeadAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/example.cs"), "first"));
        PageTemplate first = (await storage.GetPageAsync(Key("/example.cs")))!;

        await storage.SavePageAsync(new PageTemplate(Key("/example.cs"), "first"));
        PageTemplate second = (await storage.GetPageAsync(Key("/example.cs")))!;

        Assert.That(second.Template, Is.EqualTo("first"));
        Assert.That(second.RevisionId, Is.Not.EqualTo(first.RevisionId));
        Assert.That(second.Hash, Is.Not.EqualTo(first.Hash));
        Assert.That((await storage.GetRevisionByHashAsync(first.Hash))?.RevisionId, Is.EqualTo(first.RevisionId));
        Assert.That((await storage.GetHeadAsync(Key("/example.cs")))?.IsDeleted, Is.False);
        Assert.That(await database.Set<PageRevision>().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task ConditionalSaveRejectsAStaleHashWithoutAddingARevisionAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        PagePath path = Key("/conditional");
        await storage.SavePageAsync(new PageTemplate(path, "first"));
        string expectedHash = (await storage.GetPageAsync(path))!.Hash;

        Assert.That(await storage.TrySavePageAsync(new PageTemplate(path, "second"), expectedHash), Is.True);
        Assert.That(await storage.TrySavePageAsync(new PageTemplate(path, "stale"), expectedHash), Is.False);

        Assert.That((await storage.GetPageAsync(path))?.Template, Is.EqualTo("second"));
        Assert.That(await database.Set<PageRevision>().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task DistinctKeysAreSeparatePagesAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);

        await storage.SavePageAsync(new PageTemplate(Key("/example.cs"), "first"));
        await storage.SavePageAsync(new PageTemplate(Key("/other.cs"), "second"));

        Assert.That((await storage.GetPageAsync(Key("/example.cs")))?.Template, Is.EqualTo("first"));
        Assert.That((await storage.GetPageAsync(Key("/other.cs")))?.Template, Is.EqualTo("second"));
        Assert.That(await database.Set<PageHead>().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task ConcurrentSavesForTheSameNewPathLeaveOneRowAsync() {
        const int Writers = 8;
        PagePath path = Key("/raced");
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task[] saves = [.. Enumerable.Range(0, Writers).Select(index => Task.Run(async () => {
            string template = $"template-{index}";
            PageImage image = new(TemplateFingerprint.Of(template), "1:build", [(byte)index], [(byte)(index + 1)], []);
            await using StorageDbContext database = new(_options);
            IPageStore storage = new SqlitePageStore(database);

            await gate.Task;
            await storage.SavePageAsync(new PageTemplate(path, template, image));
        }))];

        gate.SetResult();
        await Assert.DoesNotThrowAsync(() => Task.WhenAll(saves));

        await using StorageDbContext reader = new(_options);
        PageRevision[] rows = [.. await reader.Set<PageRevision>().ToArrayAsync()];

        Assert.That(rows, Has.Length.EqualTo(Writers));
        Assert.That(rows, Has.All.Matches<PageRevision>(row => row is not null && row.TemplateHash == TemplateFingerprint.Of(row.Template)));
        PageHead head = (await reader.Set<PageHead>().SingleAsync(row => row.Path == path.Key))!;
        Assert.That(rows, Has.Some.Matches<PageRevision>(row => row is not null && row.RevisionId == head.RevisionId));
    }

    [Test]
    public async Task ConcurrentSavesForDifferentPathsKeepBothPagesAsync() {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task[] saves = [
            SaveAsync("/first", "first"),
            SaveAsync("/second", "second"),
        ];

        gate.SetResult();
        await Task.WhenAll(saves);

        await using StorageDbContext reader = new(_options);
        IPageStore storage = new SqlitePageStore(reader);

        Assert.That((await storage.GetPageAsync(Key("/first")))?.Template, Is.EqualTo("first"));
        Assert.That((await storage.GetPageAsync(Key("/second")))?.Template, Is.EqualTo("second"));

        async Task SaveAsync(string key, string template) {
            await using StorageDbContext database = new(_options);
            await gate.Task;
            await new SqlitePageStore(database).SavePageAsync(new PageTemplate(Key(key), template));
        }
    }

    [Test]
    public async Task ReapplyingMigrationsPreservesDataAsync() {
        await using (StorageDbContext writer = new(_options)) {
            IPageStore storage = new SqlitePageStore(writer);
            await storage.SavePageAsync(new PageTemplate(Key("/example.cs"), "saved"));
        }

        await using StorageDbContext database = new(_options);
        await database.Database.MigrateAsync();

        Assert.That(await database.Database.GetAppliedMigrationsAsync(), Is.Not.Empty);
        Assert.That((await new SqlitePageStore(database).GetPageAsync(Key("/example.cs")))?.Template, Is.EqualTo("saved"));
    }

    [Test]
    public async Task HeadCannotReferenceARevisionAtAnotherPathAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/first"), "first"));
        PageTemplate page = (await storage.GetPageAsync(Key("/first")))!;

        await Assert.ThrowsAsync<DbUpdateException>(async () => {
            database.Add(new PageHead { Path = "/second", RevisionId = page.RevisionId });
            await database.SaveChangesAsync();
        });
    }

    [Test]
    public async Task CompilationSurvivesReopeningAsync() {
        PageImage image = new(TemplateFingerprint.Of("Hello."), "1:build", [1, 2, 3], [4, 5, 6], []);

        await using (StorageDbContext writer = new(_options)) {
            IPageStore storage = new SqlitePageStore(writer);
            await storage.SavePageAsync(new PageTemplate(Key("/hello"), "Hello.", image));
        }

        await using StorageDbContext reader = new(_options);
        PageImage? actual = (await new SqlitePageStore(reader).GetPageAsync(Key("/hello")))?.Image;

        Assert.That(actual, Is.Not.Null);
        Assert.That(actual?.TemplateHash, Is.EqualTo(image.TemplateHash));
        Assert.That(actual?.RuntimeId, Is.EqualTo("1:build"));
        Assert.That(actual?.Bytes, Is.EqualTo(image.Bytes));
        Assert.That(actual?.Symbols, Is.EqualTo(image.Symbols));
        Assert.That(actual?.Diagnostics, Is.Empty);
    }

    [Test]
    public async Task RevisionHistoryListsMetadataAndReadsOnlySelectedTemplateAsync() {
        PagePath path = Key("/history");
        PageTemplate first = new(path, "first version", new PageImage(
            TemplateFingerprint.Of("first version"), "1:runtime", [1, 2, 3], [4, 5, 6], []));
        PageTemplate second = new(path, "second <version>");
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(first);
        await storage.SavePageAsync(second);

        IReadOnlyList<PageRevisionSummary> history = await storage.ListRevisionsAsync(path);
        PageRevisionTemplate? selected = await storage.GetRevisionTemplateByHashAsync(first.Hash);
        string[] expectedHashes = [second.Hash, first.Hash];

        Assert.That(history.Select(revision => revision.Hash), Is.EqualTo(expectedHashes));
        Assert.That(history[0].ImageStatus, Is.EqualTo("Not compiled"));
        Assert.That(history[1].ImageStatus, Is.EqualTo("Compiled"));
        Assert.That(history[0].IsCurrent, Is.True);
        Assert.That(history[1].IsCurrent, Is.False);
        Assert.That(selected?.Path, Is.EqualTo(path));
        Assert.That(selected?.Template, Is.EqualTo("first version"));
        Assert.That(await storage.GetRevisionTemplateByHashAsync("missing"), Is.Null);
    }

    [Test]
    public async Task CompileDiagnosticsSurviveReopeningAsync() {
        PageImage failure = new(TemplateFingerprint.Of("<% broken"), "1:build", null, null, [
            new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(2, 16), "CS1525: broken"),
            new PageDiagnostic(PageDiagnosticSeverity.Warning, new TemplatePosition(3, 5), "CS0219: unused"),
        ]);

        await using (StorageDbContext writer = new(_options)) {
            IPageStore storage = new SqlitePageStore(writer);
            await storage.SavePageAsync(new PageTemplate(Key("/broken"), "<% broken", failure));
        }

        await using StorageDbContext reader = new(_options);
        PageImage? actual = (await new SqlitePageStore(reader).GetPageAsync(Key("/broken")))?.Image;

        Assert.That(actual?.Bytes, Is.Null);
        Assert.That(actual?.Diagnostics, Is.EqualTo(failure.Diagnostics).AsCollection);
    }

    [Test]
    public async Task TrySetCompilationStoresTheResultAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/hello"), "Hello."));
        PageTemplate page = (await storage.GetPageAsync(Key("/hello")))!;
        PageImage image = new(TemplateFingerprint.Of("Hello."), "1:build", [4, 5, 6], [7, 8, 9], []);

        bool stored = await storage.TrySetImageAsync(page.RevisionId, "Hello.", image);

        PageTemplate? updated = await storage.GetPageAsync(Key("/hello"));
        Assert.That(stored, Is.True);
        Assert.That(updated?.Hash, Is.EqualTo(page.Hash));
        Assert.That(updated?.RevisionId, Is.EqualTo(page.RevisionId));
        Assert.That(updated?.Image?.Bytes, Is.EqualTo(image.Bytes));
        Assert.That(updated?.Image?.Symbols, Is.EqualTo(image.Symbols));
        Assert.That(updated?.Image?.RuntimeId, Is.EqualTo("1:build"));
    }

    [Test]
    public async Task ACompileForAnOlderRevisionDoesNotMoveTheNewerHeadAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/raced"), "older"));
        PageTemplate older = (await storage.GetPageAsync(Key("/raced")))!;
        await storage.SavePageAsync(new PageTemplate(Key("/raced"), "newer"));
        PageTemplate newer = (await storage.GetPageAsync(Key("/raced")))!;
        PageImage image = new(TemplateFingerprint.Of("older"), "runtime", [1], [2], []);

        bool stored = await storage.TrySetImageAsync(older.RevisionId, "older", image);

        PageTemplate? current = await storage.GetPageAsync(Key("/raced"));
        PageTemplate? historical = await storage.GetRevisionByHashAsync(older.Hash);
        Assert.That(stored, Is.True);
        Assert.That(current?.RevisionId, Is.EqualTo(newer.RevisionId));
        Assert.That(current?.Template, Is.EqualTo("newer"));
        Assert.That((await storage.GetHeadAsync(Key("/raced")))?.Path, Is.EqualTo(newer.Path));
        Assert.That(historical?.Hash, Is.EqualTo(older.Hash));
        Assert.That(historical?.Image?.Bytes, Is.EqualTo(image.Bytes));
    }

    [Test]
    public async Task TrySetImageRefusesWhenTheTemplateChangedAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/hello"), "newer"));
        PageTemplate savedPage = (await storage.GetPageAsync(Key("/hello")))!;
        PageImage image = new(TemplateFingerprint.Of("older"), "1:build", [1], [2], []);

        bool stored = await storage.TrySetImageAsync(savedPage.RevisionId, "older", image);

        PageTemplate? page = await storage.GetPageAsync(Key("/hello"));
        Assert.That(stored, Is.False);
        Assert.That(page?.Template, Is.EqualTo("newer"));
        Assert.That(page?.Image, Is.Null);
    }

    [Test]
    public async Task TrySetCompilationRefusesAMissingPageAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        PageImage image = new(TemplateFingerprint.Of("template"), "1:build", [1], [2], []);

        bool stored = await storage.TrySetImageAsync(Guid.NewGuid().ToString("N"), "template", image);

        Assert.That(stored, Is.False);
        Assert.That(await storage.GetPageAsync(Key("/missing")), Is.Null);
    }

    [Test]
    public async Task DeleteHidesTheHeadButRetainsItsRevisionAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/deleted"), "retained"));
        PageTemplate saved = (await storage.GetPageAsync(Key("/deleted")))!;

        bool deleted = await storage.DeletePageAsync(Key("/deleted"));

        Assert.That(deleted, Is.True);
        Assert.That(await storage.GetPageAsync(Key("/deleted")), Is.Null);
        Assert.That((await storage.GetHeadAsync(Key("/deleted")))?.IsDeleted, Is.True);
        Assert.That((await storage.GetRevisionByHashAsync(saved.Hash))?.Template, Is.EqualTo("retained"));
        Assert.That(await storage.DeletePageAsync(Key("/deleted")), Is.False);
    }

    [Test]
    public async Task SavingAfterDeletionContinuesTheRetainedHistoryAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/recreated"), "first"));
        PageTemplate first = (await storage.GetPageAsync(Key("/recreated")))!;
        await storage.DeletePageAsync(Key("/recreated"));

        await storage.SavePageAsync(new PageTemplate(Key("/recreated"), "second"));

        PageTemplate? current = await storage.GetPageAsync(Key("/recreated"));
        Assert.That(current?.Template, Is.EqualTo("second"));
        Assert.That((await storage.GetHeadAsync(Key("/recreated")))?.IsDeleted, Is.False);
        Assert.That((await storage.GetRevisionByHashAsync(first.Hash))?.Template, Is.EqualTo("first"));
        Assert.That(await database.Set<PageRevision>().CountAsync(row => row.Path == "/recreated"), Is.EqualTo(2));
    }

    [Test]
    public async Task ScrubRequiresADeletedHeadAndRemovesEveryRevisionAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/scrub"), "first"));
        string firstHash = (await storage.GetPageAsync(Key("/scrub")))!.Hash;
        await storage.SavePageAsync(new PageTemplate(Key("/scrub"), "second"));
        string secondHash = (await storage.GetPageAsync(Key("/scrub")))!.Hash;

        Assert.That(await storage.ScrubPageAsync(Key("/scrub")), Is.False);
        Assert.That((await storage.GetHeadAsync(Key("/scrub")))?.IsDeleted, Is.False);
        Assert.That(await storage.GetRevisionByHashAsync(firstHash), Is.Not.Null);
        Assert.That(await storage.GetRevisionByHashAsync(secondHash), Is.Not.Null);

        await storage.DeletePageAsync(Key("/scrub"));
        bool scrubbed = await storage.ScrubPageAsync(Key("/scrub"));

        Assert.That(scrubbed, Is.True);
        Assert.That(await storage.GetHeadAsync(Key("/scrub")), Is.Null);
        Assert.That(await storage.GetPageAsync(Key("/scrub")), Is.Null);
        Assert.That(await storage.GetRevisionByHashAsync(firstHash), Is.Null);
        Assert.That(await storage.GetRevisionByHashAsync(secondHash), Is.Null);
        Assert.That(await storage.ScrubPageAsync(Key("/scrub")), Is.False);

        await storage.SavePageAsync(new PageTemplate(Key("/deleted-scrub"), "deleted secret"));
        await storage.DeletePageAsync(Key("/deleted-scrub"));
        Assert.That(await storage.ScrubPageAsync(Key("/deleted-scrub")), Is.True);
        Assert.That(await storage.GetHeadAsync(Key("/deleted-scrub")), Is.Null);
    }

    [Test]
    public async Task ScrubDoesNotReportSuccessWhileAReaderPinsTheWalAndCanRetryAsync() {
        SqliteConnectionStringBuilder connectionString = new() { DataSource = _databasePath, Pooling = false };
        await using (SqliteConnection enableWal = new(connectionString.ToString())) {
            await enableWal.OpenAsync();
            await using SqliteCommand command = enableWal.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL";
            Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo("wal"));
        }

        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/pinned"), "reader marker"));
        await storage.DeletePageAsync(Key("/pinned"));

        await using SqliteConnection pinned = new(connectionString.ToString());
        await pinned.OpenAsync();
        await using SqliteTransaction snapshot = pinned.BeginTransaction(deferred: true);
        await using (SqliteCommand read = pinned.CreateCommand()) {
            read.Transaction = snapshot;
            read.CommandText = "SELECT \"Template\" FROM \"PageRevisions\" WHERE \"Path\" = '/pinned'";
            Assert.That(await read.ExecuteScalarAsync(), Is.EqualTo("reader marker"));
        }

        await Assert.ThrowsAsync<IOException>(() => storage.ScrubPageAsync(Key("/pinned")));
        Assert.That(await storage.GetPageAsync(Key("/pinned")), Is.Null);

        await snapshot.DisposeAsync();
        await pinned.DisposeAsync();
        Assert.That(await storage.ScrubPageAsync(Key("/pinned")), Is.False);
    }

    [Test]
    public async Task FailedRevisionDeletionRollsBackHeadDeletionAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/rollback"), "retained secret"));
        PageTemplate saved = (await storage.GetPageAsync(Key("/rollback")))!;
        await storage.DeletePageAsync(Key("/rollback"));
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_scrub BEFORE DELETE ON "PageRevisions"
            BEGIN SELECT RAISE(ABORT, 'injected scrub failure'); END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() => storage.ScrubPageAsync(Key("/rollback")));

        Assert.That((await storage.GetHeadAsync(Key("/rollback")))?.IsDeleted, Is.True);
        Assert.That((await storage.GetRevisionByHashAsync(saved.Hash))?.RevisionId, Is.EqualTo(saved.RevisionId));
        Assert.That(await storage.GetRevisionByHashAsync(saved.Hash), Is.Not.Null);
    }

    [Test]
    public async Task LateCompilationCannotResurrectAScrubbedRevisionAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new PageTemplate(Key("/late"), "secret"));
        PageTemplate saved = (await storage.GetPageAsync(Key("/late")))!;
        await storage.DeletePageAsync(Key("/late"));
        await storage.ScrubPageAsync(Key("/late"));
        PageImage image = new(TemplateFingerprint.Of("secret"), "runtime", [1], [2], []);

        bool updated = await storage.TrySetImageAsync(saved.RevisionId, saved.Template, image);

        Assert.That(updated, Is.False);
        Assert.That(await storage.GetRevisionByHashAsync(saved.Hash), Is.Null);
        Assert.That(await storage.GetHeadAsync(Key("/late")), Is.Null);
    }

    [Test]
    public async Task ScrubErasesDeletedTemplateAndBinaryMarkersFromStorageFilesAsync() {
        const string sourceMarker = "unique-source-scrub-marker";
        byte[] binaryMarker = Encoding.UTF8.GetBytes("unique-image-scrub-marker");
        PageImage image = new(TemplateFingerprint.Of(sourceMarker), "runtime", binaryMarker, binaryMarker, []);

        await using (StorageDbContext database = new(_options)) {
            IPageStore storage = new SqlitePageStore(database);
            await storage.SavePageAsync(new PageTemplate(Key("/secret"), sourceMarker, image));
            await storage.DeletePageAsync(Key("/secret"));
            Assert.That(await storage.ScrubPageAsync(Key("/secret")), Is.True);
        }

        string[] paths = [_databasePath, _databasePath + "-journal", _databasePath + "-wal"];
        foreach (string path in paths.Where(File.Exists)) {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.That(Encoding.UTF8.GetString(bytes), Does.Not.Contain(sourceMarker));
            Assert.That(bytes.AsSpan().IndexOf(binaryMarker), Is.EqualTo(-1));
        }
    }

    [Test]
    public async Task CancelledOperationsThrowAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        CancellationToken cancelled = new(true);
        PageImage image = new("hash", "1:build", [1], [2], []);

        await Assert.CatchAsync<OperationCanceledException>(() => storage.GetPageAsync(Key("/example.cs"), cancelled));
        await Assert.CatchAsync<OperationCanceledException>(() => storage.SavePageAsync(new PageTemplate(Key("/example.cs"), "template"), cancelled));
        await Assert.CatchAsync<OperationCanceledException>(() => storage.TrySetImageAsync(Guid.NewGuid().ToString("N"), "template", image, cancelled));
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
