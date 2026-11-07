// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using NUnit.Framework;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageArchiveWriterTests {

    [Test]
    public void WritingThenReadingReturnsTheSamePagesAndState() {
        byte[] bytes = PageArchiveWriter.Write([
            new PageArchiveEntry(Key("/about"), "About", [new("visits", "3")]),
            new PageArchiveEntry(Key("/"), "Home", [new("counter", "null")]),
        ]);

        using OpenArchive archive = new(bytes);

        Assert.That(TestArchive.Scalar(archive.Connection, """SELECT "Source" FROM "Pages" WHERE "Path" = '/about'"""),
            Is.EqualTo("About"));
        Assert.That(TestArchive.Scalar(archive.Connection, """SELECT "Value" FROM "PageState" WHERE "PagePath" = '/about' AND "Key" = 'visits'"""),
            Is.EqualTo("3"));
        Assert.That(TestArchive.Scalar(archive.Connection, """SELECT "Value" FROM "PageState" WHERE "PagePath" = '/' AND "Key" = 'counter'"""),
            Is.EqualTo("null"));
    }

    [Test]
    public void APageWithoutStateWritesNoStateRows() {
        byte[] bytes = PageArchiveWriter.Write([new PageArchiveEntry(Key("/about"), "About", [])]);

        using OpenArchive archive = new(bytes);

        Assert.That(TestArchive.Scalar(archive.Connection, """SELECT COUNT(*) FROM "PageState" """), Is.EqualTo(0L));
    }

    [Test]
    public void TheDatabaseCarriesTheFormatApplicationIdAndVersion() {
        byte[] bytes = PageArchiveWriter.Write([new PageArchiveEntry(Key("/about"), "About", [])]);

        using OpenArchive archive = new(bytes);

        Assert.That(TestArchive.Scalar(archive.Connection, "PRAGMA application_id"),
            Is.EqualTo((long)PageArchiveFormat.ApplicationId));
        Assert.That(TestArchive.Scalar(archive.Connection, "PRAGMA user_version"),
            Is.EqualTo((long)PageArchiveFormat.Version));
    }

    [Test]
    public void SerializedBytesAreAReadableDatabase() {
        byte[] bytes = PageArchiveWriter.Write([new PageArchiveEntry(Key("/about"), "Hello.", [])]);

        using OpenArchive archive = new(bytes);
        using SqliteCommand command = archive.Connection.CreateCommand();
        command.CommandText = """SELECT "Source" FROM "Pages" WHERE "Path" = '/about'""";

        Assert.That(command.ExecuteScalar(), Is.EqualTo("Hello."));
    }

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
