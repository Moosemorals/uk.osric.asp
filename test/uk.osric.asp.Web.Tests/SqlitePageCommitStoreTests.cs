// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Pages;
using uk.osric.asp.Web.Storage;

/// <summary>The store's one transaction, and the report a page reads from it.</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SqlitePageCommitStoreTests {
    private static DatabaseTemplate? s_template;

    private string _databasePath = string.Empty;
    private DbContextOptions<StorageDbContext> _options = new DbContextOptionsBuilder<StorageDbContext>().Options;

    [OneTimeSetUp]
    public static void MigrateOnce() {
        s_template = new DatabaseTemplate("commit");
    }

    [OneTimeTearDown]
    public static void DeleteTemplate() {
        s_template?.Dispose();
        s_template = null;
    }

    [SetUp]
    public void OpenDatabase() {
        _databasePath = s_template!.CreateFile("commit");
        _options = DatabaseTemplate.Options(_databasePath);
    }

    [TearDown]
    public void DeleteDatabase() {
        DatabaseTemplate.DeleteFiles(_databasePath);
    }

    [Test]
    public async Task ACommitStoresEveryRevisionOrNoneAsync() {
        PageTemplate first = new(Key("/one"), "first");
        PageTemplate second = new(Key("/two"), "second");

        IReadOnlyList<PageCommitAnswer> answers = await Store().CommitAsync([
            new PageCommit(PageCommitKind.Create, first.Path, Revision: first),
            new PageCommit(PageCommitKind.Create, second.Path, Revision: second),
        ]);

        Assert.Multiple(() => {
            Assert.That(answers.Select(answer => answer.Outcome), Is.EqualTo([PageCommitOutcome.Saved, PageCommitOutcome.Saved]));
            Assert.That(answers.Select(answer => answer.Hash), Is.EqualTo([first.Hash, second.Hash]));
            Assert.That(SourceOf("/one"), Is.EqualTo("first"));
            Assert.That(SourceOf("/two"), Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task ARefusedOperationWritesNothingAndUndoesWhatWentBeforeItAsync() {
        PageTemplate head = new(Key("/one"), "first");
        _ = await Store().CommitAsync([new PageCommit(PageCommitKind.Create, head.Path, Revision: head)]);
        PageTemplate second = new(Key("/two"), "second");

        IReadOnlyList<PageCommitAnswer> answers = await Store().CommitAsync([
            new PageCommit(PageCommitKind.Create, second.Path, Revision: second),
            new PageCommit(PageCommitKind.Save, head.Path, "not-the-head", new PageTemplate(head.Path, "changed")),
        ]);

        Assert.Multiple(() => {
            Assert.That(answers[0].Outcome, Is.EqualTo(PageCommitOutcome.RolledBack), "it was applied, then undone");
            Assert.That(answers[1].Outcome, Is.EqualTo(PageCommitOutcome.Stale));
            Assert.That(SourceOf("/one"), Is.EqualTo("first"), "a refusal writes nothing at all");
            Assert.That(SourceOf("/two"), Is.Null);
        });
    }

    [Test]
    public async Task ACreateAMoveAndADeleteOfOneKeyLandInOrderAsync() {
        PageTemplate created = new(Key("/a"), "first");

        IReadOnlyList<PageCommitAnswer> answers = await Store().CommitAsync([
            new PageCommit(PageCommitKind.Create, created.Path, Revision: created),
            new PageCommit(PageCommitKind.Move, Key("/b"), created.Hash, From: Key("/a"), Target: Key("/b")),
            new PageCommit(PageCommitKind.Delete, Key("/b")),
        ]);

        Assert.Multiple(() => {
            Assert.That(answers.Select(answer => answer.Outcome),
                Is.EqualTo([PageCommitOutcome.Saved, PageCommitOutcome.Moved, PageCommitOutcome.Deleted]),
                "the move read the page the create wrote, and the delete the head the move left");
            Assert.That(RevisionsAt("/a"), Is.Zero, "nothing is left at the old key");
            Assert.That(RevisionsAt("/b"), Is.EqualTo(1), "the revision the move re-filed is under the new key");
            Assert.That(DeletedAt("/b"), Is.True, "and the head there is the tombstone the delete left");
        });
    }

    [Test]
    public async Task AMoveAndATombstoneLandInTheSameTransactionAsync() {
        PageTemplate moved = new(Key("/moved"), "moved");
        PageTemplate deleted = new(Key("/deleted"), "deleted");
        _ = await Store().CommitAsync([
            new PageCommit(PageCommitKind.Create, moved.Path, Revision: moved),
            new PageCommit(PageCommitKind.Create, deleted.Path, Revision: deleted),
        ]);

        IReadOnlyList<PageCommitAnswer> answers = await Store().CommitAsync([
            new PageCommit(PageCommitKind.Move, Key("/elsewhere"), moved.Hash, From: moved.Path, Target: Key("/elsewhere")),
            new PageCommit(PageCommitKind.Delete, deleted.Path),
        ]);

        Assert.Multiple(() => {
            Assert.That(answers.Select(answer => answer.Outcome), Is.EqualTo([PageCommitOutcome.Moved, PageCommitOutcome.Deleted]));
            Assert.That(SourceOf("/moved"), Is.Null, "nothing is left at the old key");
            Assert.That(SourceOf("/elsewhere"), Is.EqualTo("moved"));
            Assert.That(DeletedAt("/deleted"), Is.True);
        });
    }

    private IPageCommitStore Store() =>
        new SqlitePageCommitStore(DatabaseTemplate.Factory(_options), NullLogger<SqlitePageCommitStore>.Instance);

    private string? SourceOf(string path) {
        using StorageDbContext database = new(_options);

        return database.Database
            .SqlQuery<string?>($"""
                SELECT revision."Template" FROM "PageHeads" AS head
                JOIN "PageRevisions" AS revision
                    ON revision."RevisionId" = head."RevisionId" AND revision."Path" = head."Path"
                WHERE head."Path" = {path} AND head."IsDeleted" = 0
                """)
            .AsEnumerable()
            .SingleOrDefault();
    }

    private int RevisionsAt(string path) {
        using StorageDbContext database = new(_options);

        return database.Database
            .SqlQuery<int>($"""SELECT COUNT(*) AS "Value" FROM "PageRevisions" WHERE "Path" = {path}""")
            .AsEnumerable()
            .Single();
    }

    private bool DeletedAt(string path) {
        using StorageDbContext database = new(_options);

        return database.Database
            .SqlQuery<bool>($"""SELECT "IsDeleted" FROM "PageHeads" WHERE "Path" = {path}""")
            .AsEnumerable()
            .Single();
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
