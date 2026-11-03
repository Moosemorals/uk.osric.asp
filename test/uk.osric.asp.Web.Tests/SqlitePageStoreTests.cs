// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
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
    public async Task OverwriteKeepsOneFileWithLatestContentAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/example.cs"), "first"));

        await storage.SavePageAsync(new StoredPage(Key("/example.cs"), "second"));

        Assert.That((await storage.GetPageAsync(Key("/example.cs")))?.Content, Is.EqualTo("second"));
        Assert.That(await database.Set<StoredFile>().CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task DistinctKeysAreSeparatePagesAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);

        await storage.SavePageAsync(new StoredPage(Key("/example.cs"), "first"));
        await storage.SavePageAsync(new StoredPage(Key("/other.cs"), "second"));

        Assert.That((await storage.GetPageAsync(Key("/example.cs")))?.Content, Is.EqualTo("first"));
        Assert.That((await storage.GetPageAsync(Key("/other.cs")))?.Content, Is.EqualTo("second"));
        Assert.That(await database.Set<StoredFile>().CountAsync(), Is.EqualTo(2));
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
        StoredFile[] rows = [.. await reader.Set<StoredFile>().ToArrayAsync()];

        Assert.That(rows, Has.Length.EqualTo(1));
        // The row is written whole, so the image belongs to the content stored beside it.
        Assert.That(rows[0].ContentHash, Is.EqualTo(ContentFingerprint.Of(rows[0].Content)));
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
    public async Task LegacyStringDiagnosticsAreReadAsErrorsWithoutAnImageAsync() {
        await using (StorageDbContext writer = new(_options)) {
            writer.Add(new StoredFile {
                Path = Key("/legacy").Key,
                Content = "broken",
                RuntimeId = "1:build",
                ContentHash = "hash",
                Image = null,
                Diagnostics = "[\"error CS1525: legacy\"]",
            });
            await writer.SaveChangesAsync();
        }

        await using StorageDbContext reader = new(_options);
        PageCompilation? actual = (await new SqlitePageStore(reader).GetPageAsync(Key("/legacy")))?.Compilation;

        Assert.That(actual?.Diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, null, "error CS1525: legacy"),
        }));
    }

    [Test]
    public async Task LegacyStringDiagnosticsAreReadAsWarningsWithAnImageAsync() {
        await using (StorageDbContext writer = new(_options)) {
            writer.Add(new StoredFile {
                Path = Key("/legacy-warning").Key,
                Content = "warning",
                RuntimeId = "1:build",
                ContentHash = "hash",
                Image = [1, 2, 3],
                Diagnostics = "[\"warning CS0219: legacy\"]",
            });
            await writer.SaveChangesAsync();
        }

        await using StorageDbContext reader = new(_options);
        PageCompilation? actual = (await new SqlitePageStore(reader).GetPageAsync(Key("/legacy-warning")))?.Compilation;

        Assert.That(actual?.Image, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(actual?.Diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Warning, null, "warning CS0219: legacy"),
        }));
    }

    [Test]
    public async Task TrySetCompilationStoresTheResultAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/hello"), "Hello."));
        PageCompilation compilation = new(ContentFingerprint.Of("Hello."), "1:build", [4, 5, 6], [7, 8, 9], []);

        bool stored = await storage.TrySetCompilationAsync(Key("/hello"), "Hello.", compilation);

        PageCompilation? actual = (await storage.GetPageAsync(Key("/hello")))?.Compilation;
        Assert.That(stored, Is.True);
        Assert.That(actual?.Image, Is.EqualTo(compilation.Image));
        Assert.That(actual?.Symbols, Is.EqualTo(compilation.Symbols));
        Assert.That(actual?.RuntimeId, Is.EqualTo("1:build"));
    }

    [Test]
    public async Task TrySetCompilationRefusesWhenTheContentChangedAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        await storage.SavePageAsync(new StoredPage(Key("/hello"), "newer"));
        PageCompilation compilation = new(ContentFingerprint.Of("older"), "1:build", [1], [2], []);

        bool stored = await storage.TrySetCompilationAsync(Key("/hello"), "older", compilation);

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

        bool stored = await storage.TrySetCompilationAsync(Key("/missing"), "content", compilation);

        Assert.That(stored, Is.False);
        Assert.That(await storage.GetPageAsync(Key("/missing")), Is.Null);
    }

    [Test]
    public async Task CancelledOperationsThrowAsync() {
        await using StorageDbContext database = new(_options);
        IPageStore storage = new SqlitePageStore(database);
        CancellationToken cancelled = new(true);
        PageCompilation compilation = new("hash", "1:build", [1], [2], []);

        await Assert.CatchAsync<OperationCanceledException>(() => storage.GetPageAsync(Key("/example.cs"), cancelled));
        await Assert.CatchAsync<OperationCanceledException>(() => storage.SavePageAsync(new StoredPage(Key("/example.cs"), "content"), cancelled));
        await Assert.CatchAsync<OperationCanceledException>(() => storage.TrySetCompilationAsync(Key("/example.cs"), "content", compilation, cancelled));
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
