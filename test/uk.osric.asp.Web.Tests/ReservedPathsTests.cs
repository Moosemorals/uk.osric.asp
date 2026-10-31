// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class ReservedPathsTests {

    private static readonly ReservedPaths Defaults = new(ReservedPaths.Default);

    [TestCase("/edit")]
    [TestCase("/edit/page")]
    [TestCase("/edit/a/b")]
    [TestCase("/site.css")]
    [TestCase("/favicon.ico")]
    [TestCase("/heath")]
    public void DefaultKeysAreReserved(string path) {
        Assert.That(Defaults.IsReserved(PagePath.Normalize(path)), Is.True);
    }

    [TestCase("/")]
    [TestCase("/edits")]
    [TestCase("/editing")]
    [TestCase("/site.css.bak")]
    [TestCase("/hello")]
    public void SimilarKeysAreNotReserved(string path) {
        Assert.That(Defaults.IsReserved(PagePath.Normalize(path)), Is.False);
    }

    [Test]
    public void KeysAreComparedCaseInsensitively() {
        Assert.That(Defaults.IsReserved(PagePath.Normalize("/EDIT")), Is.True);
    }

    [Test]
    public void EmptyConfigurationFallsBackToTheDefaults() {
        ReservedPaths reserved = new([]);

        Assert.That(reserved.IsReserved(PagePath.Normalize("/edit")), Is.True);
    }

    [Test]
    public void MissingConfigurationFallsBackToTheDefaults() {
        ReservedPaths reserved = new(null);

        Assert.That(reserved.IsReserved(PagePath.Normalize("/site.css")), Is.True);
    }

    [Test]
    public void ConfiguredKeysReplaceTheDefaults() {
        ReservedPaths reserved = new(["/admin"]);

        Assert.That(reserved.IsReserved(PagePath.Normalize("/admin/page")), Is.True);
        Assert.That(reserved.IsReserved(PagePath.Normalize("/edit")), Is.False);
    }

    [TestCase("/Admin/")]
    [TestCase("admin")]
    public void ConfiguredKeysAreNormalised(string path) {
        ReservedPaths reserved = new([path]);

        Assert.That(reserved.IsReserved(PagePath.Normalize("/admin")), Is.True);
    }
}
