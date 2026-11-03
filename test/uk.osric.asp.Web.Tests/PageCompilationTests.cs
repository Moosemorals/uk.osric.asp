// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageCompilationTests {

    private static readonly PageCompilation s_compilation = new(
        ContentFingerprint.Of("Hello."),
        "3:1:build",
        [1, 2, 3],
        []);

    [Test]
    public void IsCurrentWhenTheContentAndBuildMatch() {
        Assert.That(s_compilation.IsCurrent(ContentFingerprint.Of("Hello."), "3:1:build"), Is.True);
    }

    [Test]
    public void IsNotCurrentWhenTheContentChanged() {
        Assert.That(s_compilation.IsCurrent(ContentFingerprint.Of("Changed."), "3:1:build"), Is.False);
    }

    [Test]
    public void IsNotCurrentWhenTheBuildChanged() {
        Assert.That(s_compilation.IsCurrent(ContentFingerprint.Of("Hello."), "4:1:build"), Is.False);
    }
}
