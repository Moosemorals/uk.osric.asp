// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Storage;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class AssistantMemoryStoreTests {

    private static DatabaseTemplate? s_template;

    private readonly TestClock _clock = new() { Now = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero) };
    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    private static MemorySource Source => new(ConversationId: 7, RunId: "run-1");

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("assistant-memory");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("assistant-memory");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task AnOwnerSeesOnlyTheirOwnMemoriesAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        await memories.RememberAsync("owner", MemoryKind.Preference, "The owner writes British English.", Source);
        await memories.RememberAsync("other", MemoryKind.Fact, "The site runs on Podman.", new MemorySource(8, "run-2"));

        IReadOnlyList<MemoryCandidate> owner = await memories.RecallAsync("owner", "\"english\"*", 50);
        IReadOnlyList<MemoryCandidate> other = await memories.RecallAsync("other", "\"english\"*", 50);

        int ownerCount = await memories.CountAsync("owner");
        int otherCount = await memories.CountAsync("other");

        Assert.Multiple(() => {
            Assert.That(owner.Select(memory => memory.Content), Is.EqualTo(["The owner writes British English."]));
            Assert.That(other, Is.Empty);
            Assert.That(ownerCount, Is.EqualTo(1));
            Assert.That(otherCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ADuplicateReturnsTheExistingIdAndDoesNotGrowTheTableAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        MemoryWriteResult first = await memories.RememberAsync("owner", MemoryKind.Preference, "Prefers  colour to color", Source);
        MemoryWriteResult second = await memories.RememberAsync("owner", MemoryKind.Preference, "prefers colour to color", Source);

        Assert.Multiple(() => {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(second.Memory!.Id, Is.EqualTo(first.Memory!.Id));
            Assert.That(RowCount(database), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheCapRefusesAtExactlyTheConfiguredNumberOfMemoriesAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database, maximumMemoriesPerOwner: 2);
        await memories.RememberAsync("owner", MemoryKind.Fact, "First fact.", Source);
        await memories.RememberAsync("owner", MemoryKind.Fact, "Second fact.", Source);

        MemoryWriteResult refused = await memories.RememberAsync("owner", MemoryKind.Fact, "Third fact.", Source);

        Assert.Multiple(() => {
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(refused.Error, Does.Contain("MaxMemoriesPerOwner"));
            Assert.That(RowCount(database), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AnUpdateKeepsThePreviousVersionAndBumpsTheHeadAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        MemoryCandidate original = (await memories.RememberAsync("owner", MemoryKind.Preference, "Prefers colour.", Source)).Memory!;

        MemoryWriteResult updated = await memories.UpdateAsync("owner", original.Id, original.Version, "Prefers colour, except in code.", new MemorySource(9, "run-9"));

        MemoryEdit[] history = await database.Set<MemoryEdit>().AsNoTracking().ToArrayAsync();
        Assert.Multiple(() => {
            Assert.That(updated.IsSuccess, Is.True, updated.Error);
            Assert.That(updated.Memory!.Version, Is.EqualTo(2));
            Assert.That(updated.Memory.Content, Is.EqualTo("Prefers colour, except in code."));
            Assert.That(history, Has.Length.EqualTo(1));
            Assert.That(history[0].MemoryId, Is.EqualTo(original.Id));
            Assert.That(history[0].Version, Is.EqualTo(1));
            Assert.That(history[0].Content, Is.EqualTo("Prefers colour."));
        });
    }

    [Test]
    public async Task AStaleVersionIsRefusedWithTheCurrentContentAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        MemoryCandidate original = (await memories.RememberAsync("owner", MemoryKind.Preference, "Prefers colour.", Source)).Memory!;
        await memories.UpdateAsync("owner", original.Id, original.Version, "Prefers colour, always.", Source);

        MemoryWriteResult stale = await memories.UpdateAsync("owner", original.Id, original.Version, "Prefers color.", Source);
        MemoryCandidate current = (await memories.RecallAsync("owner", "\"colour\"*", 50)).Single();

        Assert.Multiple(() => {
            Assert.That(stale.IsSuccess, Is.False);
            Assert.That(stale.Error, Does.Contain("Prefers colour, always."));
            Assert.That(current.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AForgetRemovesTheHeadTheHistoryAndTheIndexRowAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        MemoryCandidate original = (await memories.RememberAsync("owner", MemoryKind.Preference, "Prefers a deep blue.", Source)).Memory!;
        await memories.UpdateAsync("owner", original.Id, original.Version, "Prefers a deep navy.", Source);

        bool forgotten = await memories.ForgetAsync("owner", original.Id);

        Assert.Multiple(() => {
            Assert.That(forgotten, Is.True);
            Assert.That(RowCount(database), Is.Zero);
            Assert.That(database.Set<MemoryEdit>().Count(), Is.Zero);
        });
        Assert.That(await memories.RecallAsync("owner", "\"navy\"*", 50), Is.Empty);
        AssertIndexIsConsistent(database);
    }

    [Test]
    public async Task AMemoryOfAnotherOwnerIsNotThereToForgetOrUpdateAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        MemoryCandidate original = (await memories.RememberAsync("owner", MemoryKind.Fact, "The site runs on Podman.", Source)).Memory!;

        bool forgotten = await memories.ForgetAsync("other", original.Id);
        MemoryWriteResult updated = await memories.UpdateAsync("other", original.Id, 1, "Changed.", Source);

        Assert.Multiple(() => {
            Assert.That(forgotten, Is.False);
            Assert.That(updated.IsSuccess, Is.False);
            Assert.That(RowCount(database), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ClearProvenanceKeepsTheMemoriesAndDropsWhereTheyCameFromAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        await memories.RememberAsync("owner", MemoryKind.Fact, "The site runs on Podman.", Source);
        await memories.RememberAsync("owner", MemoryKind.Fact, "The site runs behind Caddy.", new MemorySource(8, "run-2"));

        int cleared = await memories.ClearProvenanceAsync(Source.ConversationId);

        MemoryEntry[] rows = await database.Set<MemoryEntry>().AsNoTracking().ToArrayAsync();
        Assert.Multiple(() => {
            Assert.That(cleared, Is.EqualTo(1));
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows.Single(row => row.Content.Contains("Podman")).SourceConversationId, Is.Null);
            Assert.That(rows.Single(row => row.Content.Contains("Podman")).SourceRunId, Is.Null);
            Assert.That(rows.Single(row => row.Content.Contains("Caddy")).SourceConversationId, Is.EqualTo(8));
        });
    }

    [Test]
    public async Task DeleteForOwnerRemovesOneOwnersMemoriesAndLeavesAnothersAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        await memories.RememberAsync("owner", MemoryKind.Fact, "The site runs on Podman.", Source);
        await memories.RememberAsync("other", MemoryKind.Fact, "Other's note.", new MemorySource(8, "run-2"));

        int removed = await memories.DeleteForOwnerAsync("owner");
        int ownerCount = await memories.CountAsync("owner");
        int otherCount = await memories.CountAsync("other");

        Assert.Multiple(() => {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(ownerCount, Is.Zero);
            Assert.That(otherCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ARankedRecallReturnsTheDocumentedOrderAndTheStickyListFollowsTheConversationAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        MemoryCandidate deploy = (await memories.RememberAsync("owner", MemoryKind.Fact,
            "The deploy script runs inside the deployment container.", Source)).Memory!;
        MemoryCandidate colour = (await memories.RememberAsync("owner", MemoryKind.Preference,
            "The owner prefers colour over color.", Source)).Memory!;

        IReadOnlyList<MemoryCandidate> matches = await memories.RecallAsync("owner", "\"deploy\"*", 50);
        await memories.TouchAsync("owner", [colour.Id], conversationId: 11);

        IReadOnlyList<MemoryCandidate> sticky = await memories.StickyAsync("owner", 11);
        IReadOnlyList<MemoryCandidate> anotherConversation = await memories.StickyAsync("owner", 12);

        Assert.Multiple(() => {
            Assert.That(matches.Select(memory => memory.Id), Is.EqualTo([deploy.Id]));
            Assert.That(sticky.Select(memory => memory.Id), Is.EqualTo([colour.Id]));
            Assert.That(sticky.Single().RecallCount, Is.EqualTo(1));
            Assert.That(sticky.Single().LastUsed, Is.EqualTo(_clock.Now));
            Assert.That(anotherConversation, Is.Empty);
        });
    }

    [Test]
    public async Task ATouchChangesNoIndexRowAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        MemoryCandidate remembered = (await memories.RememberAsync("owner", MemoryKind.Fact,
            "The site runs on Podman.", Source)).Memory!;
        long before = IndexRowId(database, remembered.Id);

        await memories.TouchAsync("owner", [remembered.Id], conversationId: 11);

        Assert.That(IndexRowId(database, remembered.Id), Is.EqualTo(before));
        AssertIndexIsConsistent(database);
    }

    [Test]
    public async Task PinnedMemoriesAreReadNewestChangeFirstAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);
        MemoryCandidate older = (await memories.RememberAsync("owner", MemoryKind.Fact, "Older fact.", Source)).Memory!;
        MemoryCandidate newer = (await memories.RememberAsync("owner", MemoryKind.Fact, "Newer fact.", Source)).Memory!;
        await database.Set<MemoryEntry>().Where(memory => memory.Id == older.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(memory => memory.Pinned, true));
        _clock.Now = _clock.Now.AddMinutes(5);
        await database.Set<MemoryEntry>().Where(memory => memory.Id == newer.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(memory => memory.Pinned, true)
                .SetProperty(memory => memory.UpdatedAt, _clock.Now));

        Assert.That((await memories.PinnedAsync("owner")).Select(memory => memory.Id), Is.EqualTo([newer.Id, older.Id]));
    }

    [Test]
    public async Task ASecretIsRefusedAndLeavesTheStoreAloneAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database);

        MemoryWriteResult refused = await memories.RememberAsync("owner", MemoryKind.Reference, "api_key=7f3c9a1b2e", Source);

        Assert.Multiple(() => {
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(refused.Error, Does.Contain("password or key assignment"));
            Assert.That(RowCount(database), Is.Zero);
        });
    }

    [Test]
    public async Task AMemoryLongerThanTheCapIsRefusedRatherThanTruncatedAsync() {
        await using StorageDbContext database = new(_options);
        IMemoryStore memories = Store(database, maximumMemoryChars: 20);

        MemoryWriteResult refused = await memories.RememberAsync("owner", MemoryKind.Fact, new string('a', 21), Source);

        Assert.Multiple(() => {
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(refused.Error, Does.Contain("20"));
            Assert.That(RowCount(database), Is.Zero);
        });
    }

    private SqliteMemoryStore Store(
        StorageDbContext database,
        int maximumMemoriesPerOwner = 200,
        int maximumMemoryChars = 1_000) =>
        new(database, new AssistantOptions {
            MaxMemoriesPerOwner = maximumMemoriesPerOwner,
            MaxMemoryChars = maximumMemoryChars,
            MaxMemoriesPerRun = 12,
            MaxMemoryCharsPerRun = 4_000,
            MaxMemorySearchResults = 50,
            MaxMemoryQueryTerms = 12,
        }, _clock);

    private static int RowCount(StorageDbContext database) =>
        database.Set<MemoryEntry>().Count();

    // FTS5's own check, which is what proves the triggers and the index agree after a write.
    private static void AssertIndexIsConsistent(StorageDbContext database) =>
        Assert.DoesNotThrow(() => database.Database.ExecuteSqlRaw(
            "INSERT INTO \"AssistantMemorySearch\"(\"AssistantMemorySearch\") VALUES('integrity-check');"));

    private static long IndexRowId(StorageDbContext database, long memoryId) =>
        database.Database
            .SqlQueryRaw<long>("SELECT \"rowid\" AS \"Value\" FROM \"AssistantMemorySearch\" WHERE \"rowid\" = {0}", memoryId)
            .Single();
}
