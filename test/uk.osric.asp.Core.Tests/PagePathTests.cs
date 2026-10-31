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
