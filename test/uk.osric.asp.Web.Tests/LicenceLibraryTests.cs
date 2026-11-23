// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Admin;

/// <summary>Proves the image's files are one ordered list of documents.</summary>
[TestFixture]
public sealed class LicenceLibraryTests {

    private static readonly SiteDocumentKind[] ExpectedKinds = [
        SiteDocumentKind.SiteLicence,
        SiteDocumentKind.ThirdPartyNotices,
        SiteDocumentKind.LicenceText,
        SiteDocumentKind.LicenceText,
        SiteDocumentKind.ReuseAnnotation,
    ];

    private static readonly string[] ExpectedNames = [
        "LICENSE", "THIRD-PARTY-NOTICES.md", "ISC", "MIT", "REUSE.toml",
    ];

    private static readonly string[] OnlyTheNotices = ["THIRD-PARTY-NOTICES.md"];

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder() {
        _folder = Path.Combine(Path.GetTempPath(), "licences-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Path.Combine(_folder, "LICENSES"));
    }

    [TearDown]
    public void DeleteFolder() {
        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public void EveryFileTheImageCarriesIsADocumentInAFixedOrder() {
        Write("LICENSE", "MIT License");
        Write("THIRD-PARTY-NOTICES.md", "## Notices");
        Write("REUSE.toml", "version = 1");
        Write(Path.Combine("LICENSES", "MIT.txt"), "Permission is hereby granted");
        Write(Path.Combine("LICENSES", "ISC.txt"), "Permission to use");

        IReadOnlyList<SiteDocument> documents = Library().Documents;

        Assert.Multiple(() => {
            Assert.That(documents.Select(document => document.Kind), Is.EqualTo(ExpectedKinds));
            Assert.That(documents.Select(document => document.Name), Is.EqualTo(ExpectedNames),
                "the licence texts are in name order");
            Assert.That(documents[1].Format, Is.EqualTo(SiteDocumentFormat.Markdown), "the notices are Markdown");
            Assert.That(documents[0].Format, Is.EqualTo(SiteDocumentFormat.Text));
            Assert.That(documents[3].Text, Is.EqualTo("Permission is hereby granted"));
        });
    }

    [Test]
    public void AMissingFileIsLeftOutAndTheRestStillAnswer() {
        Write("LICENSE", "MIT License");

        IReadOnlyList<SiteDocument> documents = Library().Documents;

        Assert.Multiple(() => {
            Assert.That(documents, Has.Count.EqualTo(1));
            Assert.That(documents[0].Name, Is.EqualTo("LICENSE"));
            Assert.That(Library().SiteLicence, Is.EqualTo("MIT License"), "the route's own members still answer");
            Assert.That(Library().ThirdPartyNotices, Is.Null);
        });
    }

    // A folder where a file is expected fails the same way on every platform the suite runs on, and the
    // library leaves it out rather than caching the failure.
    [Test]
    public void AnUnreadableFileIsLeftOutRatherThanCachedAsAFailure() {
        _ = Directory.CreateDirectory(Path.Combine(_folder, "LICENSE"));
        Write("THIRD-PARTY-NOTICES.md", "## Notices");

        LicenceLibrary library = Library();

        Assert.Multiple(() => {
            Assert.That(library.SiteLicence, Is.Null);
            Assert.That(library.Documents.Select(document => document.Name), Is.EqualTo(OnlyTheNotices));
            Assert.That(library.Documents, Has.Count.EqualTo(1), "a second read answers the same list");
        });
    }

    private LicenceLibrary Library() => new(_folder, NullLogger<LicenceLibrary>.Instance);

    private void Write(string name, string text) {
        string path = Path.Combine(_folder, name);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
