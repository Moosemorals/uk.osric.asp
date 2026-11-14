// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageArchiveReaderTests {

    [Test]
    public void AValidArchiveIsReadIntoKeysSourcesAndState() {
        byte[] bytes = TestArchive.Build(
            [("/about", "About"), ("/", "Home")],
            [("/about", "visits", "3"), ("/about", "counter", "null")]);

        PageArchiveReadResult result = Reader().Read(bytes);

        Assert.That(result.Problems, Is.Empty);
        Assert.That(result.Pages.Select(page => page.Path.Key), Is.EqualTo(new List<string> { "/", "/about" }));
        Assert.That(result.Pages[1].Source, Is.EqualTo("About"));
        Assert.That(result.Pages[1].State.Select(value => value.Key), Is.EqualTo(new List<string> { "counter", "visits" }));
        Assert.That(result.Pages[1].State[0].Value, Is.EqualTo("null"));
    }

    [Test]
    public void AFileThatIsNotASqliteDatabaseIsRejected() {
        PageArchiveReadResult result = Reader().Read(Encoding.UTF8.GetBytes("This is not a database."));

        AssertRejected(result, "SQLite");
    }

    [Test]
    public void ADatabaseWithoutTheFormatIdentityIsRejected() {
        byte[] bytes = TestArchive.Archive(
            TestArchive.CreateTables,
            applicationId: 0x12345678);

        AssertRejected(Reader().Read(bytes), "identity");
    }

    [Test]
    public void AnotherUserVersionIsRejected() {
        // A future version, so the reader has to refuse it before it reads a row.
        byte[] bytes = TestArchive.Archive(connection => {
            TestArchive.CreateTables(connection);
            TestArchive.Execute(connection, """INSERT INTO "Pages" ("Path", "Source") VALUES ('/about', 'About')""");
        }, version: PageArchiveFormat.Version + 1);

        AssertRejected(Reader().Read(bytes), "version");
    }

    [Test]
    public void APathThatIsNotNormalisedIsRejected() {
        byte[] bytes = TestArchive.Build([("/About", "About")]);

        AssertRejected(Reader().Read(bytes), "/About");
    }

    [Test]
    public void AReservedPathIsRejected() {
        byte[] bytes = TestArchive.Build([("/admin/import", "Admin")]);

        AssertRejected(Reader().Read(bytes), "reserved");
    }

    [Test]
    public void TheAdminIndexIsNotReservedSoAnArchiveMayCarryIt() {
        byte[] bytes = TestArchive.Build([("/admin", "Admin")]);

        PageArchiveReadResult result = Reader().Read(bytes);

        Assert.That(result.Problems, Is.Empty);
        Assert.That(result.Pages.Single().Path.Key, Is.EqualTo("/admin"));
    }

    [Test]
    public void AStateRowWithoutAPageIsRejected() {
        byte[] bytes = TestArchive.Build(
            [("/about", "About")],
            [("/ghost", "visits", "1")]);

        AssertRejected(Reader().Read(bytes), "/ghost");
    }

    [Test]
    public void AStateKeyOverTheCapIsRejected() {
        byte[] bytes = TestArchive.Build(
            [("/about", "About")],
            [("/about", new string('k', 24), "1")]);

        AssertRejected(Reader(storage: TestOptions.Storage with { MaxKeyChars = 16 }).Read(bytes), "state key");
    }

    [Test]
    public void AStateValueOverTheCapIsRejected() {
        byte[] bytes = TestArchive.Build(
            [("/about", "About")],
            [("/about", "visits", $"\"{new string('v', 64)}\"")]);

        AssertRejected(Reader(storage: TestOptions.Storage with { MaxValueBytes = 16 }).Read(bytes), "state value");
    }

    [Test]
    public void AStateValueThatIsNotJsonIsRejected() {
        byte[] bytes = TestArchive.Build(
            [("/about", "About")],
            [("/about", "visits", "not json")]);

        AssertRejected(Reader().Read(bytes), "not JSON");
    }

    [Test]
    public void ANonTextValueIsRejected() {
        byte[] bytes = TestArchive.Archive(connection => {
            TestArchive.CreateTables(connection);
            TestArchive.Execute(connection, """INSERT INTO "Pages" ("Path", "Source") VALUES ('/about', X'00FF')""");
        });

        AssertRejected(Reader().Read(bytes), "non-text");
    }

    [Test]
    public void AnInvalidUtf8ValueIsRejected() {
        byte[] bytes = TestArchive.Archive(connection => {
            TestArchive.CreateTables(connection);
            TestArchive.Execute(connection, """INSERT INTO "Pages" ("Path", "Source") VALUES ('/about', CAST(X'FF' AS TEXT))""");
        });

        AssertRejected(Reader().Read(bytes), "UTF-8");
    }

    [Test]
    public void TooManyPagesAreRejected() {
        byte[] bytes = TestArchive.Build([("/one", "1"), ("/two", "2"), ("/three", "3")]);

        AssertRejected(Reader(TestOptions.Archive with { MaxPages = 2 }).Read(bytes), "import limit");
    }

    [Test]
    public void AViewNamedPagesIsRejected() {
        byte[] bytes = TestArchive.Archive(connection => {
            TestArchive.Execute(connection, PageArchiveFormat.CreatePageState);
            TestArchive.Execute(connection, """
                CREATE VIEW "Pages" AS SELECT "PagePath" AS "Path", "Value" AS "Source" FROM "PageState"
                """);
        });

        AssertRejected(Reader().Read(bytes), "view");
    }

    [Test]
    public void AnExtraTableIsRejected() {
        byte[] bytes = TestArchive.Archive(connection => {
            TestArchive.CreateTables(connection);
            TestArchive.Execute(connection, """CREATE TABLE "Extra" ("Value" TEXT NOT NULL)""");
        });

        AssertRejected(Reader().Read(bytes), "Extra");
    }

    [Test]
    public void AnExtraColumnIsRejected() {
        byte[] bytes = TestArchive.Archive(connection => {
            TestArchive.Execute(connection, """
                CREATE TABLE "Pages" (
                    "Path"   TEXT NOT NULL PRIMARY KEY,
                    "Source" TEXT NOT NULL,
                    "Extra"  TEXT NOT NULL
                ) WITHOUT ROWID
                """);
            TestArchive.Execute(connection, PageArchiveFormat.CreatePageState);
        });

        AssertRejected(Reader().Read(bytes), "columns");
    }

    [Test]
    public void AMissingPageStateTableIsRejected() {
        byte[] bytes = TestArchive.Archive(connection => {
            TestArchive.Execute(connection, PageArchiveFormat.CreatePages);
            TestArchive.Execute(connection, """INSERT INTO "Pages" ("Path", "Source") VALUES ('/about', 'About')""");
        });

        AssertRejected(Reader().Read(bytes), "\"PageState\"");
    }

    [Test]
    public void AnArchiveWithNoPagesIsRejected() {
        byte[] bytes = TestArchive.Build([]);

        AssertRejected(Reader().Read(bytes), "no pages");
    }

    [Test]
    public void AnEmptyFileIsRejected() {
        AssertRejected(Reader().Read([]), "empty");
    }

    private static void AssertRejected(PageArchiveReadResult result, string fragment) {
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Pages, Is.Empty);
        Assert.That(result.Problems, Is.Not.Empty);
        Assert.That(string.Join(" ", result.Problems), Does.Contain(fragment));
    }

    private static PageArchiveReader Reader(ArchiveOptions? options = null, PageStorageOptions? storage = null) =>
        new(options ?? TestOptions.Archive,
            storage ?? TestOptions.Storage,
            new ReservedPaths(ReservedPaths.Default),
            NullLogger<PageArchiveReader>.Instance);
}
