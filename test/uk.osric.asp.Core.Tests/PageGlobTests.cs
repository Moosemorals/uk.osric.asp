// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageGlobTests {

    [TestCase("/vim/*", "/vim/vim.js", true)]
    [TestCase("/vim/*", "/vim", false, "a segment of the pattern is one segment of the key")]
    [TestCase("/vim/*", "/vim/a/b", false, "a star never crosses a /")]
    [TestCase("/vim/*.js", "/vim/vim.js", true)]
    [TestCase("/vim/*.js", "/vim/vim.css", false)]
    [TestCase("/*.js", "/vim.js", true)]
    [TestCase("/*.js", "/vim/vim.js", false)]
    [TestCase("/vim/?.js", "/vim/a.js", true)]
    [TestCase("/vim/?.js", "/vim/ab.js", false)]
    [TestCase("/*/*", "/a/b", true)]
    [TestCase("/*/*", "/a", false)]
    [TestCase("/vim/", "/vim", true, "a trailing slash is not a segment")]
    [TestCase("/vim/*", "/", false, "the root is no segment at all")]
    public void MatchesOneSegmentAtATime(string pattern, string key, bool expected, string? why = null) {
        Assert.That(PageGlob.IsMatch(pattern, key), Is.EqualTo(expected), why);
    }

    [Test]
    public void APatternWithNoWildcardMatchesOnlyTheKeyItSpells() {
        Assert.Multiple(() => {
            Assert.That(PageGlob.IsMatch("/vim/vim.js", "/vim/vim.js"), Is.True);
            Assert.That(PageGlob.IsMatch("/vim/vim.js", "/vim/vim.css"), Is.False);
        });
    }

    [Test]
    public void APatternIsNormalisedAsAPathIs() {
        Assert.Multiple(() => {
            Assert.That(PageGlob.IsMatch("/Vim/*", "/vim/vim.js"), Is.True, "a key is lowercase, so a pattern is too");
            Assert.That(PageGlob.IsMatch("//vim//*", "/vim/vim.js"), Is.True, "empty segments collapse");
            Assert.That(PageGlob.IsMatch("/vim/../vim/*", "/vim/vim.js"), Is.True, "so does a dot-dot");
        });
    }
}
