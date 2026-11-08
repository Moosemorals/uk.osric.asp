// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
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

        PageArchiveReadResult result = Reader().Read(bytes);

        Assert.That(result.Problems, Is.Empty);
        Assert.That(result.Pages.Select(page => page.Path.Key), Is.EqualTo(new List<string> { "/", "/about" }));
        Assert.That(result.Pages[0].Source, Is.EqualTo("Home"));
        Assert.That(result.Pages[0].State, Is.EqualTo(new List<PageStateValue> { new("counter", "null") }));
        Assert.That(result.Pages[1].Source, Is.EqualTo("About"));
        Assert.That(result.Pages[1].State, Is.EqualTo(new List<PageStateValue> { new("visits", "3") }));
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

    private static PageArchiveReader Reader() =>
        new(new ArchiveOptions(), new PageStorageOptions(), new ReservedPaths(ReservedPaths.Default), NullLogger<PageArchiveReader>.Instance);

    private static PagePath Key(string path) => PagePath.Normalize(path);
}
