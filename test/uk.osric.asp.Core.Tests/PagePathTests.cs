// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public class PagePathTests {

    [TestCase(" ", "/")]
    [TestCase("", "/")]
    [TestCase("..", "/")]
    [TestCase("...", "/...")]
    [TestCase("../", "/")]
    [TestCase("../../a", "/a")]
    [TestCase("/..a", "/..a")]
    [TestCase("//", "/")]
    [TestCase("//a///b", "/a/b")]
    [TestCase("/CAF\u00c9", "/caf\u00e9")]
    [TestCase("/Hello", "/hello")]
    [TestCase("/a..", "/a..")]
    [TestCase("/a/b/../", "/a")]
    [TestCase("/cafe\u0301", "/caf\u00e9")]
    [TestCase("/hello", "/hello")]
    [TestCase("/hello/", "/hello")]
    [TestCase("/hello/../goodbye", "/goodbye")]
    [TestCase("\t", "/")]
    [TestCase("a/../../b", "/b")]
    [TestCase("hello/../goodbye", "/goodbye")]
    [TestCase(@"/a\b", @"/a\b")]
    [TestCase(null, "/")]
    public void PagePathNormalizesAsExpected(string? input, string expected) {

        PagePath actual = PagePath.Normalize(input);

        Assert.That(actual.Path, Is.EqualTo(expected));
    }

    [TestCase("/Hello/")]
    [TestCase("//a///b")]
    [TestCase("../../a")]
    [TestCase("/a/b/../")]
    [TestCase("/cafe\u0301")]
    public void NormalizingACanonicalKeyChangesNothing(string input) {

        string once = PagePath.Normalize(input).Path;
        string twice = PagePath.Normalize(once).Path;

        Assert.That(twice, Is.EqualTo(once));
    }

    [Test]
    public void AQueryStringIsPartOfThePathAndIsNotStripped() {

        // Normalisation does not guess at URL structure: routing hands over Request.Path, which never
        // carries a query, so a '?' here came from an encoded %3F. The key keeps it and is simply not
        // addressable, which is the shell's cue to answer 404 rather than serve a mangled name.
        PagePath actual = PagePath.Normalize("/hello?from=rss");

        Assert.That(actual.Path, Is.EqualTo("/hello?from=rss"));
        Assert.That(actual.IsAddressable, Is.False);
    }

    [TestCase("/hello?from=rss")]
    [TestCase("/hello#top")]
    [TestCase("/hello?q=1#top")]
    public void ADelimiterMakesAKeyUnaddressable(string path) {

        Assert.That(PagePath.Normalize(path).IsAddressable, Is.False);
    }

    [TestCase("/")]
    [TestCase("/hello")]
    [TestCase("/hello world")]
    [TestCase("/hello%20world")]
    [TestCase("/caf\u00e9")]
    public void APathIsAddressable(string path) {

        Assert.That(PagePath.Normalize(path).IsAddressable, Is.True);
    }

    [Test]
    public void KeysDifferingOnlyByCaseAreEqual() {

        Assert.That(PagePath.Normalize("/A"), Is.EqualTo(PagePath.Normalize("/a")));
    }

    [Test]
    public void ToStringReturnsTheKey() {

        Assert.That(PagePath.Normalize("/A/").ToString(), Is.EqualTo("/a"));
    }

    [Test]
    [SetCulture("tr-TR")]
    public void LowercasingIsCultureIndependent() {

        Assert.That(PagePath.Normalize("/I").Path, Is.EqualTo("/i"));
    }
}
