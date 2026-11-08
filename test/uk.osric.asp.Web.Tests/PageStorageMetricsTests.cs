// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.Metrics;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
[NonParallelizable]
public sealed class PageStorageMetricsTests {
    [Test]
    public async Task PayloadCountsUtf8TextAndBlobsAcrossRevisionsAndPageStateAsync() {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE PageRevisions (RevisionId TEXT PRIMARY KEY, Path TEXT, Hash TEXT,
                SavedAt TEXT, Template TEXT, CreationRuntimeId TEXT, TemplateHash TEXT,
                RuntimeId TEXT, Diagnostics TEXT, Image BLOB, Symbols BLOB);
            INSERT INTO PageRevisions VALUES ('r', '/p', 'h', 'date', char(233), 'c', 'ch', 'rt', '[]', zeroblob(10), zeroblob(5));
            INSERT INTO PageRevisions VALUES ('s', '/p', 'i', 'date', '', 'c', NULL, NULL, NULL, NULL, NULL);
            CREATE TABLE PageState (PagePath TEXT, Key TEXT, Value TEXT, UpdatedAt TEXT);
            INSERT INTO PageState VALUES ('p', 'k', 'v', 'date');
            """;
        await command.ExecuteNonQueryAsync();

        (long payload, long allocated) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);

        // 41 bytes of revisions plus 7 of page state: one byte each for the path, key and value, and
        // four for the timestamp.
        Assert.That(payload, Is.EqualTo(48));
        Assert.That(allocated, Is.GreaterThan(payload));
    }

    [Test]
    public async Task EmptyRevisionsHaveZeroPayloadButAllocatedDatabasePagesAsync() {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using ServiceProvider services = CreateServices(connection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        StorageDbContext database = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
        await database.Database.EnsureCreatedAsync();

        (long payload, long allocated) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size()";

        Assert.That(payload, Is.Zero);
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
        (long beforePayload, long beforeAllocated) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);
        await store.DeletePageAsync(PagePath.Normalize("/large"));

        await store.ScrubPageAsync(PagePath.Normalize("/large"));
        (long payload, long allocated) = await PageStorageSampler.ReadSizesAsync(connection, CancellationToken.None);

        Assert.That(beforePayload, Is.GreaterThan(131072));
        Assert.That(payload, Is.Zero);
        Assert.That(allocated, Is.EqualTo(beforeAllocated));
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

    private static ServiceProvider CreateServices(SqliteConnection connection) {
        ServiceCollection services = new();
        services.AddDbContext<StorageDbContext>(options => options.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    private static MeterListener Listen(Dictionary<string, long> values) {
        MeterListener listener = new() {
            InstrumentPublished = (instrument, owner) => {
                if (instrument.Meter.Name == PageMetrics.MeterName && instrument.Name.StartsWith("page.storage.", StringComparison.Ordinal)) {
                    Assert.That(instrument.Unit, Is.EqualTo("By"));
                    Assert.That(instrument.IsObservable, Is.True);
                    owner.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => {
            Assert.That(tags.Length, Is.Zero);
            values[instrument.Name] = value;
        });
        listener.Start();
        return listener;
    }
}
