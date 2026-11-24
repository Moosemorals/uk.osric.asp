// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary><c>StoreSecrets</c> and the paths that take its rows with the page.</summary>
[TestFixture]
// Each test copies the fixture's migrated template, so the per-test path needs a per-test instance.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class SqliteStoreSecretsTests {

    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() => s_template = new DatabaseTemplate("secrets");

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("secrets");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() => DatabaseTemplate.DeleteFiles(_databasePath);

    [Test]
    public async Task ASecretRoundTripsAsync() {
        IStoreSecrets store = Secrets();
        await WriteAsync(store, "page:/a", "signing", "a private key");

        StoreSecretRow? found = await store.FindAsync("page:/a", "signing");

        Assert.Multiple(() => {
            Assert.That(found, Is.Not.Null);
            Assert.That(found!.Value, Is.EqualTo("a private key"));
            Assert.That(found.PageKey, Is.EqualTo("/a"));
        });
    }

    [Test]
    public async Task ARewrittenNameReplacesTheValueAsync() {
        IStoreSecrets store = Secrets();
        await WriteAsync(store, "page:/a", "signing", "first");
        await WriteAsync(store, "page:/a", "signing", "second");

        Assert.That((await store.FindAsync("page:/a", "signing"))!.Value, Is.EqualTo("second"));
    }

    [Test]
    public async Task AnotherPagesSecretIsNotVisibleAsync() {
        IStoreSecrets store = Secrets();
        await WriteAsync(store, "page:/a", "signing", "a's key");

        Assert.That(await store.FindAsync("page:/b", "signing"), Is.Null);
        Assert.That(await store.ListAsync("page:/b", 10), Is.Empty);
    }

    [Test]
    public async Task NamesAreOrderedOrdinallyAsync() {
        IStoreSecrets store = Secrets();
        await WriteAsync(store, "page:/a", "zeta", "1");
        await WriteAsync(store, "page:/a", "alpha", "2");

        IReadOnlyList<StoreSecretRow> rows = await store.ListAsync("page:/a", 10);

        Assert.That(rows.Select(row => row.Name), Is.EqualTo(new List<string> { "alpha", "zeta" }));
    }

    [Test]
    public async Task AListStopsAtItsLimitAsync() {
        IStoreSecrets store = Secrets();
        await WriteAsync(store, "page:/a", "one", "1");
        await WriteAsync(store, "page:/a", "two", "2");

        Assert.That(await store.ListAsync("page:/a", 1), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AValueOverItsLimitIsRefusedNamingTheSettingAsync() {
        IStoreSecrets store = Secrets();
        PageSecretOptions limits = TestOptions.Stores.Secrets with { MaxSecretBytes = 64 };

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await WriteAsync(store, "page:/a", "signing", new string('x', 65), limits)))!;

        Assert.That(refused.Message, Does.Contain("Pages:Stores:Secrets:MaxSecretBytes is 64"));
    }

    [Test]
    public async Task ANameCountOverItsLimitIsRefusedNamingTheSettingAsync() {
        IStoreSecrets store = Secrets();
        PageSecretOptions limits = TestOptions.Stores.Secrets with { MaxSecrets = 2 };
        await WriteAsync(store, "page:/a", "one", "1", limits);
        await WriteAsync(store, "page:/a", "two", "2", limits);

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await WriteAsync(store, "page:/a", "three", "3", limits)))!;

        await Assert.MultipleAsync(async () => {
            Assert.That(refused.Message, Does.Contain("Pages:Stores:Secrets:MaxSecrets secrets (2)"));
            Assert.That(await store.FindAsync("page:/a", "three"), Is.Null);
        });
    }

    [Test]
    public async Task ASubjectByteTotalOverItsLimitIsRefusedNamingTheSettingAsync() {
        IStoreSecrets store = Secrets();
        PageSecretOptions limits = TestOptions.Stores.Secrets with { MaxSubjectBytes = 100 };
        await WriteAsync(store, "page:/a", "one", new string('x', 50), limits);

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await WriteAsync(store, "page:/a", "two", new string('x', 50), limits)))!;

        Assert.That(refused.Message, Does.Contain("may use Pages:Stores:Secrets:MaxSubjectBytes (100)"));
    }

    [Test]
    public async Task RewritingANameUnderAFullPageIsAllowedAsync() {
        IStoreSecrets store = Secrets();
        PageSecretOptions limits = TestOptions.Stores.Secrets with { MaxSecrets = 1 };
        await WriteAsync(store, "page:/a", "signing", "first", limits);

        await WriteAsync(store, "page:/a", "signing", "second", limits);

        Assert.That((await store.FindAsync("page:/a", "signing"))!.Value, Is.EqualTo("second"));
    }

    [Test]
    public async Task APageDeletedBeforeTheWriteRefusesItAsync() {
        IStoreSecrets store = Secrets();

        ArgumentException refused = (await Assert.ThrowsAsync<ArgumentException>(
            async () => await WriteAsync(store, "page:/gone", "signing", "a key", requireLivePage: true)))!;

        Assert.That(refused.Message, Does.Contain("/gone was deleted before its secret could be written"));
    }

    [Test]
    public async Task DeletingAMissingSecretAnswersFalseAsync() {
        IStoreSecrets store = Secrets();

        Assert.That(await store.DeleteAsync("page:/a", "signing"), Is.False);
    }

    [Test]
    public async Task RemovingAPageTakesItsSecretsAndLeavesAnotherPageAsync() {
        IStoreSecrets store = Secrets();
        await WriteAsync(store, "page:/doomed", "signing", "doomed key");
        await WriteAsync(store, "page:/kept", "signing", "kept key");

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.DeletePageAsync(database, PagePath.Normalize("/doomed"), CancellationToken.None);
        }

        await Assert.MultipleAsync(async () => {
            Assert.That(await store.FindAsync("page:/doomed", "signing"), Is.Null);
            Assert.That(await store.FindAsync("page:/kept", "signing"), Is.Not.Null);
        });
    }

    [Test]
    public async Task MovingAPageReKeysItsSecretsAsync() {
        IStoreSecrets store = Secrets();
        await WriteAsync(store, "page:/from", "signing", "moved key");

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.MovePageAsync(
                database, PagePath.Normalize("/from"), PagePath.Normalize("/to"), CancellationToken.None);
        }

        await Assert.MultipleAsync(async () => {
            Assert.That(await store.FindAsync("page:/from", "signing"), Is.Null);
            StoreSecretRow? moved = await store.FindAsync("page:/to", "signing");
            Assert.That(moved, Is.Not.Null);
            Assert.That(moved!.PageKey, Is.EqualTo("/to"));
        });
    }

    [Test]
    public async Task RemovingASubjectTakesItsSecretsAsync() {
        IStoreSecrets store = Secrets();
        await WriteAsync(store, "page:/a", "signing", "a key");

        await using (StorageDbContext database = new(_options)) {
            _ = await StoreValueSql.DeleteSubjectAsync(database, "page:/a", CancellationToken.None);
        }

        Assert.That(await store.FindAsync("page:/a", "signing"), Is.Null);
    }

    private IStoreSecrets Secrets() => new SqliteStoreSecrets(
        DatabaseTemplate.Factory(_options),
        TestOptions.Stores,
        TimeProvider.System);

    private static Task WriteAsync(
        IStoreSecrets store,
        string subject,
        string name,
        string value,
        PageSecretOptions? limits = null,
        bool requireLivePage = false) {
        string pageKey = subject[StoreSubjectKeys.PagePrefix.Length..];

        return store.WriteAsync(
            new StoreSecretWrite(subject, pageKey, name, value, requireLivePage),
            limits ?? TestOptions.Stores.Secrets);
    }
}
