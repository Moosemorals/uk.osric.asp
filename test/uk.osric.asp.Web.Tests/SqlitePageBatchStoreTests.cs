// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqlitePageBatchStoreTests {
    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("batch");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("batch");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task EveryPageIsVisibleOnlyAfterTheCommit() {
        await DatabaseTemplate.SavePageAsync(_options, "/one", "old one");
        await DatabaseTemplate.SavePageAsync(_options, "/two", "old two");

        string? seenDuringCommit = null;
        ObservingStore store = Store();
        store.OnStateWrite = async () => {
            await using StorageDbContext observer = new(_options);
            seenDuringCommit = await new SqlitePageStore(observer, NullLogger<SqlitePageStore>.Instance).GetSourceAsync(Key("/one"));
        };

        PageBatchResult result = await store.SaveBatchAsync([
            Batch("/one", "new one", [new("visits", "1")]),
            Batch("/two", "new two", []),
        ]);

        Assert.That(result.IsCommitted, Is.True);
        Assert.That(seenDuringCommit, Is.EqualTo("old one"), "a reader must see the old page until the commit");

        await using StorageDbContext database = new(_options);
        IPageStore pages = new SqlitePageStore(database, NullLogger<SqlitePageStore>.Instance);
        Assert.That(await pages.GetSourceAsync(Key("/one")), Is.EqualTo("new one"));
        Assert.That(await pages.GetSourceAsync(Key("/two")), Is.EqualTo("new two"));
    }

    [Test]
    public async Task AFailedWriteRollsBackTheWholeBatch() {
        await DatabaseTemplate.SavePageAsync(_options, "/one", "old one");
        await DatabaseTemplate.SavePageAsync(_options, "/two", "old two");
        (string oneHead, string twoHead) = await HeadsAsync();

        ObservingStore store = Store();
        store.FailOnPage = "/two";

        Assert.That(async () => await store.SaveBatchAsync([
            Batch("/one", "new one", [new("visits", "1")]),
            Batch("/two", "new two", [new("visits", "2")]),
        ]), Throws.TypeOf<InvalidOperationException>());

        await using StorageDbContext database = new(_options);
        Assert.That(await HeadRevisionAsync(database, "/one"), Is.EqualTo(oneHead), "the first page's head must not move");
        Assert.That(await HeadRevisionAsync(database, "/two"), Is.EqualTo(twoHead), "the second page's head must not move");
        Assert.That(await database.Set<PageRevision>().CountAsync(row => row.Path == "/one"), Is.EqualTo(1));
        Assert.That(await database.Set<PageRevision>().CountAsync(row => row.Path == "/two"), Is.EqualTo(1));
        Assert.That(await database.Set<PageStateEntry>().CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task AMergedStateOverTheCapWritesNothing() {
        await DatabaseTemplate.SavePageAsync(_options, "/one", "old one");
        await DatabaseTemplate.SavePageAsync(_options, "/two", "old two");
        await new SqlitePageStateStore(DatabaseTemplate.Factory(_options), TestOptions.Storage, TimeProvider.System)
            .SetValueAsync(Key("/two"), "kept", "\"kept\"");
        (string oneHead, string twoHead) = await HeadsAsync();

        // Sixteen bytes cannot hold both the key the page already has and the one the archive brings.
        ObservingStore store = Store(TestOptions.Storage with { MaxPageBytes = 16, MaxValueBytes = 16 });

        PageBatchResult result = await store.SaveBatchAsync([
            Batch("/one", "new one", [new("visits", "1")]),
            Batch("/two", "new two", [new("added", "\"x\"")]),
        ]);

        Assert.That(result.IsCommitted, Is.False);
        Assert.That(result.Refused.Select(path => path.Key), Is.EqualTo(new List<string> { "/two" }));

        await using StorageDbContext database = new(_options);
        Assert.That(await HeadRevisionAsync(database, "/one"), Is.EqualTo(oneHead));
        Assert.That(await HeadRevisionAsync(database, "/two"), Is.EqualTo(twoHead));
        Assert.That(await database.Set<PageRevision>().CountAsync(), Is.EqualTo(2));

        IPageStateStore state = new SqlitePageStateStore(DatabaseTemplate.Factory(_options), TestOptions.Storage, TimeProvider.System);
        Assert.That(await state.ListKeysAsync(Key("/two")), Is.EqualTo(new List<string> { "kept" }));
    }

    private static PageBatchSave Batch(string path, string source, IReadOnlyList<PageStateValue> state) =>
        new(PageTemplate.NewRevision(Key(path), source, null, "test-runtime"), state);

    private ObservingStore Store(PageStorageOptions? storage = null) {
        return new ObservingStore(new StorageDbContext(_options), storage ?? TestOptions.Storage, TimeProvider.System);
    }

    private async Task<(string One, string Two)> HeadsAsync() {
        await using StorageDbContext database = new(_options);
        return (await HeadRevisionAsync(database, "/one"), await HeadRevisionAsync(database, "/two"));
    }

    private static async Task<string> HeadRevisionAsync(StorageDbContext database, string path) {
        PageHead head = await database.Set<PageHead>().AsNoTracking().SingleAsync(row => row.Path == path);
        return head.RevisionId;
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);

    /// <summary>A batch store that can observe or fail one page's state write.</summary>
    private sealed class ObservingStore(
        StorageDbContext database,
        PageStorageOptions options,
        TimeProvider clock) : SqlitePageBatchStore(database, options, clock, NullLogger<SqlitePageBatchStore>.Instance) {

        /// <summary>Runs once, inside the transaction, before the next state row is written.</summary>
        public Func<Task>? OnStateWrite { get; set; }

        /// <summary>A page whose state write throws, proving the batch rolls back.</summary>
        public string? FailOnPage { get; set; }

        internal override async Task<int> WriteStateAsync(
            StorageDbContext database,
            PagePath path,
            PageStateValue value,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken) {
            if (string.Equals(path.Key, FailOnPage, StringComparison.Ordinal)) {
                throw new InvalidOperationException("the fixture failed this write");
            }

            if (OnStateWrite is { } observe) {
                OnStateWrite = null;
                await observe();
            }

            return await base.WriteStateAsync(database, path, value, updatedAt, cancellationToken);
        }
    }
}
