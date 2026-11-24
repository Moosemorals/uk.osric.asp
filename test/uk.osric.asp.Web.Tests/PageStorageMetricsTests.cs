// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.Metrics;
using System.Text;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
[Category("Slow")]
[NonParallelizable]
public sealed class PageStorageMetricsTests {
    [Test]
    public async Task PayloadCountsUtf8TextAndBlobsAcrossRevisionsAndStoreValuesAsync() {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE PageRevisions (RevisionId TEXT PRIMARY KEY, Path TEXT, Hash TEXT,
                SavedAt TEXT, Template TEXT, CreationRuntimeId TEXT, TemplateHash TEXT,
                RuntimeId TEXT, Diagnostics TEXT, Image BLOB, Symbols BLOB);
            INSERT INTO PageRevisions VALUES ('r', '/p', 'h', 'date', char(233), 'c', 'ch', 'rt', '[]', zeroblob(10), zeroblob(5));
            INSERT INTO PageRevisions VALUES ('s', '/p', 'i', 'date', '', 'c', NULL, NULL, NULL, NULL, NULL);
            CREATE TABLE StoreValues (Subject TEXT, PageKey TEXT, Key TEXT, Lifetime INTEGER,
                ExpiresAt TEXT, Value TEXT, UpdatedAt TEXT);
            INSERT INTO StoreValues VALUES ('page:/p', '/p', 'k', 1, NULL, 'v', 'date');
            INSERT INTO StoreValues VALUES ('browser:b', '/p', 'e', 3, 'until', 'v', 'date');
            CREATE TABLE StoreObjects (Subject TEXT, Sha TEXT, Length INTEGER, ContentType TEXT,
                Lifetime INTEGER, ExpiresAt TEXT, Content BLOB, CreatedAt TEXT);
            INSERT INTO StoreObjects VALUES ('page:/p', 'sha', 3, 'application/octet-stream', 1, NULL, zeroblob(3), 'date');
            CREATE TABLE StoreMessages (Id INTEGER PRIMARY KEY, Subject TEXT, ToPageKey TEXT, Topic TEXT,
                FromPageKey TEXT, Body TEXT, Lifetime INTEGER, ExpiresAt TEXT, SentAt TEXT);
            INSERT INTO StoreMessages VALUES (1, 'session:s', '/landing', NULL, '/page', 'hi', 2, NULL, 'date');
            CREATE TABLE StoreSecrets (Subject TEXT, PageKey TEXT, Name TEXT, Value TEXT, UpdatedAt TEXT);
            INSERT INTO StoreSecrets VALUES ('page:/p', '/p', 'k', 'v', 'date');
            CREATE TABLE DeclaredTables (Id INTEGER PRIMARY KEY, SourceKey TEXT, Name TEXT);
            INSERT INTO DeclaredTables VALUES (1, '/lib', 'notes'), (2, '/lib', 'tags');
            """;
        await command.ExecuteNonQueryAsync();

        (StorageSample sample, long allocated) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);

        // 41 bytes of revisions, 37 of values, 28 of messages, 41 of objects and 15 of secrets: the
        // columns each row stores, as the sampler counts them. The declared tables are a count.
        Assert.Multiple(() => {
            Assert.That(sample.Revisions, Is.EqualTo(41));
            Assert.That(sample.Values, Is.EqualTo(37));
            Assert.That(sample.Messages, Is.EqualTo(28));
            Assert.That(sample.Objects, Is.EqualTo(41));
            Assert.That(sample.Secrets, Is.EqualTo(15));
            Assert.That(sample.DeclaredTables, Is.EqualTo(2));
            Assert.That(sample.Payload, Is.EqualTo(162));
            Assert.That(allocated, Is.GreaterThan(sample.Payload));
        });
    }

    [Test]
    public async Task EmptyRevisionsHaveZeroPayloadButAllocatedDatabasePagesAsync() {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ServiceProvider services = CreateServices(connection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await database.Database.EnsureCreatedAsync();

        (StorageSample sample, long allocated) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size()";

        Assert.That(sample.Payload, Is.Zero);
        Assert.That(allocated, Is.EqualTo((long)(await command.ExecuteScalarAsync())!).And.GreaterThan(0));
    }

    [Test]
    public async Task DeletingPayloadLeavesFreelistPagesInDatabaseSizeAsync() {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ServiceProvider services = CreateServices(connection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await database.Database.EnsureCreatedAsync();
        SqlitePageStore store = new(database, NullLogger<SqlitePageStore>.Instance);
        await store.SavePageAsync(new PageTemplate(PagePath.Normalize("/large"), new string('x', 131072)));
        (StorageSample before, long beforeAllocated) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);
        await store.DeletePageAsync(PagePath.Normalize("/large"));

        await store.ScrubPageAsync(PagePath.Normalize("/large"));
        (StorageSample after, long allocated) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);

        Assert.That(before.Payload, Is.GreaterThan(131072));
        Assert.That(after.Payload, Is.Zero);
        Assert.That(allocated, Is.EqualTo(beforeAllocated));
    }

    [Test]
    public async Task EveryShapeMovesTheFigureItBelongsToAsync() {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<StorageDbContext> contextOptions = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite(connection)
            .Options;
        IDbContextFactory<StorageDbContext> factory = new TestContextFactory(contextOptions);

        await using (StorageDbContext database = new(contextOptions)) {
            await database.Database.EnsureCreatedAsync();
            SqlitePageStore pages = new(database, NullLogger<SqlitePageStore>.Instance);
            await pages.SavePageAsync(new PageTemplate(PagePath.Normalize("/shapes"), "payload"));
            _ = await new DeclaredTableApplier(TestOptions.Stores.Records).ApplyAsync(
                database,
                PagePath.Normalize("/shapes"),
                new PageSchema([
                    new PageTable("notes", StoreTableSubject.Page, StoreTableLifetime.Page,
                        [new PageColumn("Title", PageColumnType.Text)]),
                ]));
        }

        await new SqliteStoreValues(factory, TestOptions.Stores, TimeProvider.System).WriteAsync(
            new StoreValueWrite("page:/shapes", "/shapes", "k", (int)StoreLifetimeKind.Page, null, "\"v\"", RequireLivePage: true),
            TestOptions.Stores.Page,
            CancellationToken.None);
        _ = await new SqliteStoreMessages(factory, TestOptions.Stores, TimeProvider.System).PostAsync(
            new StoreMessageWrite("session:s", "/shapes", null, "/shapes", "hi", (int)StoreLifetimeKind.Person, null),
            TestOptions.Stores.Messages,
            CancellationToken.None);

        byte[] bytes = Encoding.UTF8.GetBytes("the object");
        await new SqliteStoreObjects(factory, TestOptions.Stores, TimeProvider.System).WriteAsync(
            new StoreObjectWrite(
                "page:/shapes",
                PageObjectHash.Of(bytes),
                bytes.LongLength,
                "text/plain; charset=utf-8",
                (int)StoreLifetimeKind.Page,
                null,
                bytes,
                RequireLivePage: true),
            TestOptions.Stores.Objects,
            CancellationToken.None);
        await new SqliteStoreSecrets(factory, TestOptions.Stores, TimeProvider.System).WriteAsync(
            new StoreSecretWrite("page:/shapes", "/shapes", "signing", "text", RequireLivePage: true),
            TestOptions.Stores.Secrets,
            CancellationToken.None);

        (StorageSample sample, _) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(sample.Revisions, Is.GreaterThan(0), "the saved revision");
            Assert.That(sample.Values, Is.GreaterThan(0), "the value");
            Assert.That(sample.Messages, Is.GreaterThan(0), "the message");
            Assert.That(sample.Objects, Is.GreaterThan(0), "the object");
            Assert.That(sample.Secrets, Is.GreaterThan(0), "the secret");
            Assert.That(sample.DeclaredTables, Is.EqualTo(1), "the declared table");
            Assert.That(sample.Payload, Is.EqualTo(
                sample.Revisions + sample.Values + sample.Messages + sample.Objects + sample.Secrets));
        });
    }

    [TestCase("/var/data/pages.db", "/", true)]
    [TestCase("/var/data/pages.db", "/var/data", true)]
    [TestCase("/var/database/pages.db", "/var/data", false)]
    public void VolumeMatchesOnlyAContainingMount(string path, string root, bool expected) {
        Assert.That(PageStorageSampler.IsOnVolume(path, root), Is.EqualTo(expected));
    }

    [Test]
    public void GaugesAreAbsentBeforeTheFirstSuccessfulSample() {
        Dictionary<string, long> values = [];
        using MeterListener listener = Listen(values);
        using PageMetrics metrics = new();

        listener.RecordObservableInstruments();

        Assert.That(values, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CachedGaugesNeedNoStorageAndRetainValuesAfterSamplingFailureAsync(bool resample) {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ServiceProvider services = CreateServices(connection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await database.Database.EnsureCreatedAsync();
        Dictionary<string, long> values = [];
        using MeterListener listener = Listen(values);
        using PageMetrics metrics = new();
        using PageStorageSampler sampler = new(services.GetRequiredService<IServiceScopeFactory>(), metrics,
            new PageStorageRefresh(), NullLogger<PageStorageSampler>.Instance);
        await sampler.SampleAsync(CancellationToken.None);
        listener.RecordObservableInstruments();
        long allocated = values["page.storage.database.size"];
        await database.Database.ExecuteSqlRawAsync("DROP TABLE PageRevisions");
        if (resample) {
            await sampler.SampleAsync(CancellationToken.None);
        }
        values.Clear();

        listener.RecordObservableInstruments();

        Assert.That(values["page.storage.payload.size"], Is.Zero);
        Assert.That(values["page.storage.database.size"], Is.EqualTo(allocated).And.GreaterThan(0));
        Assert.That(values.ContainsKey("page.storage.volume.capacity"), Is.False);
    }

    [Test]
    public async Task ScrubSignalsTheSingletonRefreshAsync() {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ServiceProvider services = CreateServices(connection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await database.Database.EnsureCreatedAsync();
        PageStorageRefresh refresh = new();
        SqlitePageStore store = new(database, NullLogger<SqlitePageStore>.Instance, refresh);
        await store.SavePageAsync(new PageTemplate(PagePath.Normalize("/scrub"), "payload"));
        await store.DeletePageAsync(PagePath.Normalize("/scrub"));

        Assert.That(await store.ScrubPageAsync(PagePath.Normalize("/scrub")), Is.True);
        await refresh.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task RefreshRequestsCoalesceAndShutdownCancelsTheWaitAsync() {
        PageStorageRefresh refresh = new();
        for (int request = 0; request < 100; request++) {
            refresh.Request();
        }
        await refresh.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        using CancellationTokenSource cancellation = new();
        Task waiting = refresh.WaitAsync(cancellation.Token);

        Assert.That(waiting.IsCompleted, Is.False);
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
    }

    private sealed class TestContextFactory(DbContextOptions<StorageDbContext> options) : IDbContextFactory<StorageDbContext> {
        public StorageDbContext CreateDbContext() => new(options);

        public Task<StorageDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageDbContext(options));
    }

    private static ServiceProvider CreateServices(SqliteConnection connection) {
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    private static MeterListener Listen(Dictionary<string, long> values) {
        MeterListener listener = new() {
            InstrumentPublished = (instrument, owner) => {
                if (instrument.Meter.Name == PageMetrics.MeterName && instrument.Name.StartsWith("page.storage.", StringComparison.Ordinal)) {
                    if (instrument.Name != "page.storage.declared_tables") {
                        Assert.That(instrument.Unit, Is.EqualTo("By"));
                    }

                    Assert.That(instrument.IsObservable, Is.True);
                    owner.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => {
            if (instrument.Name == "page.storage.shape.size") {
                // One instrument carries a shape label, so a test reads the shape it asked for.
                foreach (KeyValuePair<string, object?> tag in tags) {
                    if (tag.Key == "shape") {
                        values[instrument.Name + ":" + tag.Value] = value;
                    }
                }

                return;
            }

            Assert.That(tags.Length, Is.Zero);
            values[instrument.Name] = value;
        });
        listener.Start();
        return listener;
    }
}
