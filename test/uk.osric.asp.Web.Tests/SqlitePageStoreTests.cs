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
// Each test migrates its own database, so the per-test paths need a per-test instance to hold them.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqlitePageStoreTests {
    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [SetUp]
    public async Task SetUpAsync() {
        _databasePath = Path.Combine(Path.GetTempPath(), $"storage-{Guid.NewGuid():N}.db");
        SqliteConnectionStringBuilder connection = new() { DataSource = _databasePath, Pooling = false };
        _options = new DbContextOptionsBuilder<StorageDbContext>().UseSqlite(connection.ToString()).Options;
        await using StorageDbContext database = new(_options);
        await database.Database.MigrateAsync();
    }

    [TearDown]
    public void TearDown() {
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    [Test]
    public async Task MissingPageReturnsNullAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);

        StoredPage? page = await storage.GetPageAsync(Key("/missing.cs"));

        Assert.That(page, Is.Null);
    }

    [TestCase("")]
    [TestCase("public class Example {}")]
    [TestCase("line one\nline two: \u00e9")]
    public async Task WrittenContentSurvivesReopeningAsync(string content) {
        await using (StorageDbContext writer = new(_options)) {
            IPageStore storage = new SqlitePageStore(writer);
            await storage.SavePageAsync(new StoredPage(Key("/example.cs"), content));
        }

        await using StorageDbContext reader = new(_options);
        IPageStore reopened = new SqlitePageStore(reader);
        StoredPage? page = await reopened.GetPageAsync(Key("/example.cs"));

        Assert.That(page?.Content, Is.EqualTo(content));
        Assert.That(page?.Compilation, Is.Null);
    }

    [Test]
    public async Task SavesKeepDistinctRevisionsAndAdvanceTheHeadAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/example.cs"), "first"));
        StoredPage first = (await storage.GetPageAsync(Key("/example.cs")))!;

        await storage.SavePageAsync(new StoredPage(Key("/example.cs"), "first"));
        StoredPage second = (await storage.GetPageAsync(Key("/example.cs")))!;

        Assert.That(second.Content, Is.EqualTo("first"));
        Assert.That(second.RevisionId, Is.Not.EqualTo(first.RevisionId));
        Assert.That(second.Hash, Is.Not.EqualTo(first.Hash));
        Assert.That((await storage.GetRevisionByHashAsync(first.Hash))?.RevisionId, Is.EqualTo(first.RevisionId));
        Assert.That((await storage.GetHeadAsync(Key("/example.cs")))?.IsDeleted, Is.False);
        Assert.That(await database.Set<StoredRevision>().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task DistinctKeysAreSeparatePagesAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);

        await storage.SavePageAsync(new StoredPage(Key("/example.cs"), "first"));
        await storage.SavePageAsync(new StoredPage(Key("/other.cs"), "second"));

        Assert.That((await storage.GetPageAsync(Key("/example.cs")))?.Content, Is.EqualTo("first"));
        Assert.That((await storage.GetPageAsync(Key("/other.cs")))?.Content, Is.EqualTo("second"));
        Assert.That(await database.Set<PageHead>().CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task ConcurrentSavesForTheSameNewPathLeaveOneRowAsync() {
        const int Writers = 8;
        PagePath path = Key("/raced");
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task[] saves = [.. Enumerable.Range(0, Writers).Select(index => Task.Run(async () => {
            string content = $"content-{index}";
            PageCompilation compilation = new(ContentFingerprint.Of(content), "1:build", [(byte)index], [(byte)(index + 1)], []);
            await using StorageDbContext database = new(_options);
            IPageStore storage = new SqlitePageStore(database);

            await gate.Task;
            await storage.SavePageAsync(new StoredPage(path, content, compilation));
        }))];

        gate.SetResult();
        await Assert.DoesNotThrowAsync(() => Task.WhenAll(saves));

        await using StorageDbContext reader = new(_options);
        StoredRevision[] rows = [.. await reader.Set<StoredRevision>().ToArrayAsync()];

        Assert.That(rows, Has.Length.EqualTo(Writers));
        Assert.That(rows, Has.All.Matches<StoredRevision>(row => row is not null && row.ContentHash == ContentFingerprint.Of(row.Content)));
        PageHead head = (await reader.Set<PageHead>().SingleAsync(row => row.Path == path.Key))!;
        Assert.That(rows, Has.Some.Matches<StoredRevision>(row => row is not null && row.RevisionId == head.RevisionId));
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

        Assert.That((await storage.GetPageAsync(Key("/first")))?.Content, Is.EqualTo("first"));
        Assert.That((await storage.GetPageAsync(Key("/second")))?.Content, Is.EqualTo("second"));

        async Task SaveAsync(string key, string content) {
            await using StorageDbContext database = new(_options);
            await gate.Task;
            await new SqlitePageStore(database).SavePageAsync(new StoredPage(Key(key), content));
        }
    }

    [Test]
    public async Task ReapplyingMigrationsPreservesDataAsync() {
        await using (StorageDbContext writer = new(_options)) {
            IPageStore storage = new SqlitePageStore(writer);
            await storage.SavePageAsync(new StoredPage(Key("/example.cs"), "saved"));
        }

        await using StorageDbContext database = new(_options);
        await database.Database.MigrateAsync();

        Assert.That(await database.Database.GetAppliedMigrationsAsync(), Is.Not.Empty);
        Assert.That((await new SqlitePageStore(database).GetPageAsync(Key("/example.cs")))?.Content, Is.EqualTo("saved"));
    }

    [Test]
    public async Task MigrationPreservesCompiledFailedAndUncompiledLegacyRowsAsync() {
        string legacyPath = Path.Combine(Path.GetTempPath(), $"legacy-storage-{Guid.NewGuid():N}.db");
        DbContextOptions<StorageDbContext> legacyOptions = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = legacyPath, Pooling = false }.ToString())
            .Options;

        try {
            await using (StorageDbContext legacy = new(legacyOptions)) {
                await legacy.Database.MigrateAsync("20261103091947_AddPageSymbols");
                await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Files" ("Path", "Content", "ContentHash", "RuntimeId", "Image", "Symbols", "Diagnostics")
                    VALUES ('/compiled', 'compiled source', 'compiled-fingerprint', 'runtime-old', {new byte[] { 1, 2, 3 }}, {new byte[] { 4, 5, 6 }}, '["warning CS0219: old"]');
                    INSERT INTO "Files" ("Path", "Content", "ContentHash", "RuntimeId", "Image", "Symbols", "Diagnostics")
                    VALUES ('/failed', 'failed source', 'failed-fingerprint', 'runtime-failed', NULL, NULL, '["error CS1525: old"]');
                    INSERT INTO "Files" ("Path", "Content", "ContentHash", "RuntimeId", "Image", "Symbols", "Diagnostics")
                    VALUES ('/uncompiled', 'plain source', NULL, NULL, NULL, NULL, NULL);
                    """);
                await legacy.Database.MigrateAsync();
            }

            await using StorageDbContext migrated = new(legacyOptions);
            IPageStore store = new SqlitePageStore(migrated);
            StoredPage compiled = (await store.GetPageAsync(Key("/compiled")))!;
            StoredPage failed = (await store.GetPageAsync(Key("/failed")))!;
            StoredPage uncompiled = (await store.GetPageAsync(Key("/uncompiled")))!;
            IReadOnlyList<PageHeadSummary> heads = await store.ListPagesAsync();

            Assert.Multiple(() => {
                Assert.That(compiled.Content, Is.EqualTo("compiled source"));
                Assert.That(compiled.CreationRuntimeId, Is.EqualTo("runtime-old"));
                Assert.That(compiled.Compilation?.Image, Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(compiled.Compilation?.Symbols, Is.EqualTo(new byte[] { 4, 5, 6 }));
                Assert.That(compiled.Compilation?.Diagnostics.Single().Message, Is.EqualTo("warning CS0219: old"));
                Assert.That(failed.Compilation?.Image, Is.Null);
                Assert.That(failed.Compilation?.Diagnostics.Single().Message, Is.EqualTo("error CS1525: old"));
                Assert.That(uncompiled.CreationRuntimeId, Is.EqualTo("legacy:unknown"));
                Assert.That(uncompiled.Compilation, Is.Null);
                Assert.That(compiled.Hash, Does.Match("^[0-9a-f]{64}$"));
                Assert.That(heads, Has.Count.EqualTo(3));
            });
        } finally {
            File.Delete(legacyPath);
            File.Delete(legacyPath + "-wal");
            File.Delete(legacyPath + "-shm");
        }
    }

    [Test]
    public async Task HeadCannotReferenceARevisionAtAnotherPathAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/first"), "first"));
        StoredPage page = (await storage.GetPageAsync(Key("/first")))!;

        await Assert.ThrowsAsync<DbUpdateException>(async () => {
            database.Add(new PageHead { Path = "/second", RevisionId = page.RevisionId });
            await database.SaveChangesAsync();
        });
    }

    [Test]
    public async Task CompilationSurvivesReopeningAsync() {
        PageCompilation compilation = new(ContentFingerprint.Of("Hello."), "1:build", [1, 2, 3], [4, 5, 6], []);

        await using (StorageDbContext writer = new(_options)) {
            IPageStore storage = new SqlitePageStore(writer);
            await storage.SavePageAsync(new StoredPage(Key("/hello"), "Hello.", compilation));
        }

        await using StorageDbContext reader = new(_options);
        PageCompilation? actual = (await new SqlitePageStore(reader).GetPageAsync(Key("/hello")))?.Compilation;

        Assert.That(actual, Is.Not.Null);
        Assert.That(actual?.ContentHash, Is.EqualTo(compilation.ContentHash));
        Assert.That(actual?.RuntimeId, Is.EqualTo("1:build"));
        Assert.That(actual?.Image, Is.EqualTo(compilation.Image));
        Assert.That(actual?.Symbols, Is.EqualTo(compilation.Symbols));
        Assert.That(actual?.Diagnostics, Is.Empty);
    }

    [Test]
    public async Task RevisionHistoryListsMetadataAndReadsOnlySelectedSourceAsync() {
        PagePath path = Key("/history");
        StoredPage first = new(path, "first version", new PageCompilation(
            ContentFingerprint.Of("first version"), "1:runtime", [1, 2, 3], [4, 5, 6], []));
        StoredPage second = new(path, "second <version>");
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(first);
        await storage.SavePageAsync(second);

        IReadOnlyList<PageRevisionSummary> history = await storage.ListRevisionsAsync(path);
        PageRevisionSource? selected = await storage.GetRevisionSourceByHashAsync(first.Hash);
        string[] expectedHashes = [second.Hash, first.Hash];

        Assert.That(history.Select(revision => revision.Hash), Is.EqualTo(expectedHashes));
        Assert.That(history[0].CompilationStatus, Is.EqualTo("Not compiled"));
        Assert.That(history[1].CompilationStatus, Is.EqualTo("Compiled"));
        Assert.That(history[0].IsCurrent, Is.True);
        Assert.That(history[1].IsCurrent, Is.False);
        Assert.That(selected?.Path, Is.EqualTo(path));
        Assert.That(selected?.Content, Is.EqualTo("first version"));
        Assert.That(await storage.GetRevisionSourceByHashAsync("missing"), Is.Null);
    }

    [Test]
    public async Task CompileDiagnosticsSurviveReopeningAsync() {
        PageCompilation failure = new(ContentFingerprint.Of("<% broken"), "1:build", null, null, [
            new PageDiagnostic(PageDiagnosticSeverity.Error, new TemplatePosition(2, 16), "CS1525: broken"),
            new PageDiagnostic(PageDiagnosticSeverity.Warning, new TemplatePosition(3, 5), "CS0219: unused"),
        ]);

        await using (StorageDbContext writer = new(_options)) {
            IPageStore storage = new SqlitePageStore(writer);
            await storage.SavePageAsync(new StoredPage(Key("/broken"), "<% broken", failure));
        }

        await using StorageDbContext reader = new(_options);
        PageCompilation? actual = (await new SqlitePageStore(reader).GetPageAsync(Key("/broken")))?.Compilation;

        Assert.That(actual?.Image, Is.Null);
        Assert.That(actual?.Diagnostics, Is.EqualTo(failure.Diagnostics).AsCollection);
    }

    [Test]
    public async Task TrySetCompilationStoresTheResultAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/hello"), "Hello."));
        StoredPage page = (await storage.GetPageAsync(Key("/hello")))!;
        PageCompilation compilation = new(ContentFingerprint.Of("Hello."), "1:build", [4, 5, 6], [7, 8, 9], []);

        bool stored = await storage.TrySetCompilationAsync(page.RevisionId, "Hello.", compilation);

        StoredPage? updated = await storage.GetPageAsync(Key("/hello"));
        Assert.That(stored, Is.True);
        Assert.That(updated?.Hash, Is.EqualTo(page.Hash));
        Assert.That(updated?.RevisionId, Is.EqualTo(page.RevisionId));
        Assert.That(updated?.Compilation?.Image, Is.EqualTo(compilation.Image));
        Assert.That(updated?.Compilation?.Symbols, Is.EqualTo(compilation.Symbols));
        Assert.That(updated?.Compilation?.RuntimeId, Is.EqualTo("1:build"));
    }

    [Test]
    public async Task ACompileForAnOlderRevisionDoesNotMoveTheNewerHeadAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/raced"), "older"));
        StoredPage older = (await storage.GetPageAsync(Key("/raced")))!;
        await storage.SavePageAsync(new StoredPage(Key("/raced"), "newer"));
        StoredPage newer = (await storage.GetPageAsync(Key("/raced")))!;
        PageCompilation compilation = new(ContentFingerprint.Of("older"), "runtime", [1], [2], []);

        bool stored = await storage.TrySetCompilationAsync(older.RevisionId, "older", compilation);

        StoredPage? current = await storage.GetPageAsync(Key("/raced"));
        StoredPage? historical = await storage.GetRevisionByHashAsync(older.Hash);
        Assert.That(stored, Is.True);
        Assert.That(current?.RevisionId, Is.EqualTo(newer.RevisionId));
        Assert.That(current?.Content, Is.EqualTo("newer"));
        Assert.That((await storage.GetHeadAsync(Key("/raced")))?.Path, Is.EqualTo(newer.Path));
        Assert.That(historical?.Hash, Is.EqualTo(older.Hash));
        Assert.That(historical?.Compilation?.Image, Is.EqualTo(compilation.Image));
    }

    [Test]
    public async Task TrySetCompilationRefusesWhenTheContentChangedAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/hello"), "newer"));
        StoredPage savedPage = (await storage.GetPageAsync(Key("/hello")))!;
        PageCompilation compilation = new(ContentFingerprint.Of("older"), "1:build", [1], [2], []);

        bool stored = await storage.TrySetCompilationAsync(savedPage.RevisionId, "older", compilation);

        StoredPage? page = await storage.GetPageAsync(Key("/hello"));
        Assert.That(stored, Is.False);
        Assert.That(page?.Content, Is.EqualTo("newer"));
        Assert.That(page?.Compilation, Is.Null);
    }

    [Test]
    public async Task TrySetCompilationRefusesAMissingPageAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        PageCompilation compilation = new(ContentFingerprint.Of("content"), "1:build", [1], [2], []);

        bool stored = await storage.TrySetCompilationAsync(Guid.NewGuid().ToString("N"), "content", compilation);

        Assert.That(stored, Is.False);
        Assert.That(await storage.GetPageAsync(Key("/missing")), Is.Null);
    }

    [Test]
    public async Task DeleteHidesTheHeadButRetainsItsRevisionAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/deleted"), "retained"));
        StoredPage saved = (await storage.GetPageAsync(Key("/deleted")))!;

        bool deleted = await storage.DeletePageAsync(Key("/deleted"));

        Assert.That(deleted, Is.True);
        Assert.That(await storage.GetPageAsync(Key("/deleted")), Is.Null);
        Assert.That((await storage.GetHeadAsync(Key("/deleted")))?.IsDeleted, Is.True);
        Assert.That((await storage.GetRevisionByHashAsync(saved.Hash))?.Content, Is.EqualTo("retained"));
        Assert.That(await storage.DeletePageAsync(Key("/deleted")), Is.False);
    }

    [Test]
    public async Task SavingAfterDeletionContinuesTheRetainedHistoryAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/recreated"), "first"));
        StoredPage first = (await storage.GetPageAsync(Key("/recreated")))!;
        await storage.DeletePageAsync(Key("/recreated"));

        await storage.SavePageAsync(new StoredPage(Key("/recreated"), "second"));

        StoredPage? current = await storage.GetPageAsync(Key("/recreated"));
        Assert.That(current?.Content, Is.EqualTo("second"));
        Assert.That((await storage.GetHeadAsync(Key("/recreated")))?.IsDeleted, Is.False);
        Assert.That((await storage.GetRevisionByHashAsync(first.Hash))?.Content, Is.EqualTo("first"));
        Assert.That(await database.Set<StoredRevision>().CountAsync(row => row.Path == "/recreated"), Is.EqualTo(2));
    }

    [Test]
    public async Task ScrubRequiresADeletedHeadAndRemovesEveryRevisionAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/scrub"), "first"));
        string firstHash = (await storage.GetPageAsync(Key("/scrub")))!.Hash;
        await storage.SavePageAsync(new StoredPage(Key("/scrub"), "second"));
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

        await storage.SavePageAsync(new StoredPage(Key("/deleted-scrub"), "deleted secret"));
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
        await storage.SavePageAsync(new StoredPage(Key("/pinned"), "reader marker"));
        await storage.DeletePageAsync(Key("/pinned"));

        await using SqliteConnection pinned = new(connectionString.ToString());
        await pinned.OpenAsync();
        await using SqliteTransaction snapshot = pinned.BeginTransaction(deferred: true);
        await using (SqliteCommand read = pinned.CreateCommand()) {
            read.Transaction = snapshot;
            read.CommandText = "SELECT \"Content\" FROM \"PageRevisions\" WHERE \"Path\" = '/pinned'";
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
        await storage.SavePageAsync(new StoredPage(Key("/rollback"), "retained secret"));
        StoredPage saved = (await storage.GetPageAsync(Key("/rollback")))!;
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
        await storage.SavePageAsync(new StoredPage(Key("/late"), "secret"));
        StoredPage saved = (await storage.GetPageAsync(Key("/late")))!;
        await storage.DeletePageAsync(Key("/late"));
        await storage.ScrubPageAsync(Key("/late"));
        PageCompilation compilation = new(ContentFingerprint.Of("secret"), "runtime", [1], [2], []);

        bool updated = await storage.TrySetCompilationAsync(saved.RevisionId, saved.Content, compilation);

        Assert.That(updated, Is.False);
        Assert.That(await storage.GetRevisionByHashAsync(saved.Hash), Is.Null);
        Assert.That(await storage.GetHeadAsync(Key("/late")), Is.Null);
    }

    [Test]
    public async Task MigrationDoesNotLeaveLegacySecretsInTemporaryFreePagesAsync() {
        const string marker = "legacy-temporary-secret-marker";
        string temporaryCopy = _databasePath + ".temp-copy";
        await using StorageDbContext database = new(_options);
        await database.Database.MigrateAsync("20261103091947_AddPageSymbols");
        await database.Database.OpenConnectionAsync();
        await database.Database.ExecuteSqlRawAsync("PRAGMA secure_delete=OFF;");
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Files" ("Path", "Content") VALUES ('/legacy-secret', {string.Concat(Enumerable.Repeat(marker, 4000))});
            """);

        await database.Database.MigrateAsync();
        SqlitePageStore storage = new(database);
        await storage.DeletePageAsync(Key("/legacy-secret"));
        await storage.ScrubPageAsync(Key("/legacy-secret"));

        try {
            await using SqliteConnection copy = new(new SqliteConnectionStringBuilder { DataSource = temporaryCopy, Pooling = false }.ToString());
            await copy.OpenAsync();
            ((SqliteConnection)database.Database.GetDbConnection()).BackupDatabase(copy, "main", "temp");
            await copy.CloseAsync();
            Assert.That(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(temporaryCopy)).Contains(marker, StringComparison.Ordinal), Is.False, "temporary database retained the secret");
            Assert.That(Encoding.UTF8.GetString(await File.ReadAllBytesAsync(_databasePath)).Contains(marker, StringComparison.Ordinal), Is.False, "main database retained the secret");
        } finally {
            File.Delete(temporaryCopy);
        }
    }

    [Test]
    public async Task ScrubErasesDeletedSourceAndBinaryMarkersFromStorageFilesAsync() {
        const string sourceMarker = "unique-source-scrub-marker";
        byte[] binaryMarker = Encoding.UTF8.GetBytes("unique-image-scrub-marker");
        PageCompilation compilation = new(ContentFingerprint.Of(sourceMarker), "runtime", binaryMarker, binaryMarker, []);

        await using (StorageDbContext database = new(_options)) {
            IPageStore storage = new SqlitePageStore(database);
            await storage.SavePageAsync(new StoredPage(Key("/secret"), sourceMarker, compilation));
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
        PageCompilation compilation = new("hash", "1:build", [1], [2], []);

        await Assert.CatchAsync<OperationCanceledException>(() => storage.GetPageAsync(Key("/example.cs"), cancelled));
        await Assert.CatchAsync<OperationCanceledException>(() => storage.SavePageAsync(new StoredPage(Key("/example.cs"), "content"), cancelled));
        await Assert.CatchAsync<OperationCanceledException>(() => storage.TrySetCompilationAsync(Guid.NewGuid().ToString("N"), "content", compilation, cancelled));
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
