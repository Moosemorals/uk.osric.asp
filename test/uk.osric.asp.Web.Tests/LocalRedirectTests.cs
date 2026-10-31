// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Authentication;

[TestFixture]
public sealed class LocalRedirectTests {

    [TestCase("/edit")]
    [TestCase("/edit/a")]
    [TestCase("/")]
    public void LocalPathsAreSafe(string path) {
        Assert.That(LocalRedirect.IsSafe(path), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("edit")]
    [TestCase("//evil")]
    [TestCase("/\\evil")]
    [TestCase("/edit\\evil")]
    [TestCase("https://evil")]
    [TestCase("http://evil")]
    [TestCase("/edit\n")]
    public void OtherValuesAreNotSafe(string? path) {
        Assert.That(LocalRedirect.IsSafe(path), Is.False);
    }

    [Test]
    public void TheEditorIsTheFallback() {
        Assert.That(LocalRedirect.Safe("https://evil"), Is.EqualTo("/edit"));
        Assert.That(LocalRedirect.Safe(null), Is.EqualTo("/edit"));
    }

    [Test]
    public void ASafeValueIsReturnedUnchanged() {
        Assert.That(LocalRedirect.Safe("/edit/page"), Is.EqualTo("/edit/page"));
    }
}
