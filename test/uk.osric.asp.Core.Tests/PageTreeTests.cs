// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageTreeTests {

    [TestCase("/a/b", "/a", true)]
    [TestCase("/a/b/c", "/a", true)]
    [TestCase("/a", "/a", false, "a path is not below itself")]
    [TestCase("/ab", "/a", false, "a prefix that is not a segment is not a folder")]
    [TestCase("/a", "/", true, "everything but the root is below the root")]
    [TestCase("/", "/", false)]
    [TestCase("/a/b", "/", true)]
    public void IsBelowAsksWhetherAKeyLivesAnywhereUnderAPath(string key, string folder, bool expected, string? why = null) {
        Assert.That(PageTree.IsBelow(PagePath.Normalize(key), PagePath.Normalize(folder)), Is.EqualTo(expected), why);
    }

    [TestCase("/a/b", "/a", true)]
    [TestCase("/a/b/c", "/a", false, "a direct child is one segment deeper, not two")]
    [TestCase("/a", "/a", false)]
    [TestCase("/a", "/", true, "a top-level key is a direct child of the root")]
    [TestCase("/a/b", "/", false)]
    [TestCase("/ab", "/a", false)]
    public void IsDirectlyBelowAsksWhetherAKeyIsAnEntryOfAPath(string key, string folder, bool expected, string? why = null) {
        Assert.That(PageTree.IsDirectlyBelow(PagePath.Normalize(key), PagePath.Normalize(folder)), Is.EqualTo(expected), why);
    }

    [TestCase("/vim/vim.js", "/vim")]
    [TestCase("/a/b/c", "/a/b")]
    [TestCase("/a", null, "the root is always there, so a top-level key has no folder to show")]
    [TestCase("/", null, "the root has no folder")]
    public void FolderOfNamesTheFolderAKeyLivesIn(string key, string? expected, string? why = null) {
        Assert.That(PageTree.FolderOf(key), Is.EqualTo(expected), why);
    }

    [Test]
    public void FoldersAboveNamesEveryFolderAboveAKeyAndNothingElse() {
        Assert.Multiple(() => {
            Assert.That(PageTree.FoldersAbove("/a/b/c"), Is.EqualTo<string[]>(["/a", "/a/b"]));
            Assert.That(PageTree.FoldersAbove("/a"), Is.Empty);
            Assert.That(PageTree.FoldersAbove("/"), Is.Empty);
        });
    }
}
