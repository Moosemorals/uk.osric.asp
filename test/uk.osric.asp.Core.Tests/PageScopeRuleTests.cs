// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageScopeRuleTests {

    [TestCase("/a/b", "/a/b", true, "a scope that includes itself holds the path")]
    [TestCase("/a/c", "/a/b", false)]
    public void AScopeHoldsItsOwnPathOnlyWhenItIncludesItSelf(string key, string scope, bool expected, string? why = null) {
        PageScope page = new(PagePath.Normalize(scope), IncludeSelf: true, Recursive: false);

        Assert.That(page.Contains(PagePath.Normalize(key)), Is.EqualTo(expected), why);
    }

    [Test]
    public void AScopeThatDoesNotIncludeItselfStillHoldsWhatIsBelowIt() {
        PageScope scope = new(PagePath.Normalize("/a"), IncludeSelf: false, Recursive: true);

        Assert.Multiple(() => {
            Assert.That(scope.Contains(PagePath.Normalize("/a")), Is.False);
            Assert.That(scope.Contains(PagePath.Normalize("/a/b")), Is.True);
        });
    }

    [Test]
    public void AScopeThatDoesNotDescendHoldsOnlyWhatIsDirectlyBelow() {
        PageScope scope = new(PagePath.Normalize("/a"), IncludeSelf: true, Recursive: false);

        Assert.Multiple(() => {
            Assert.That(scope.Contains(PagePath.Normalize("/a")), Is.True);
            Assert.That(scope.Contains(PagePath.Normalize("/a/b")), Is.True);
            Assert.That(scope.Contains(PagePath.Normalize("/a/b/c")), Is.False);
            Assert.That(scope.Contains(PagePath.Normalize("/ab")), Is.False, "a prefix that is not a segment is another key");
        });
    }

    [Test]
    public void ADescendingScopeHoldsEveryKeyUnderItsPath() {
        PageScope scope = new(PagePath.Normalize("/a"), IncludeSelf: true, Recursive: true);

        Assert.Multiple(() => {
            Assert.That(scope.Contains(PagePath.Normalize("/a/b/c")), Is.True);
            Assert.That(scope.Contains(PagePath.Normalize("/a/b/c/d")), Is.True);
            Assert.That(scope.Contains(PagePath.Normalize("/b")), Is.False);
        });
    }

    [Test]
    public void TheRootReachesEveryOtherKeyAtEitherDepth() {
        PagePath root = PagePath.Normalize("/");
        PageScope oneLevel = new(root, IncludeSelf: true, Recursive: false);
        PageScope everything = new(root, IncludeSelf: true, Recursive: true);

        Assert.Multiple(() => {
            Assert.That(oneLevel.Contains(root), Is.True);
            Assert.That(oneLevel.Contains(PagePath.Normalize("/a")), Is.True);
            Assert.That(oneLevel.Contains(PagePath.Normalize("/a/b")), Is.False);
            Assert.That(everything.Contains(PagePath.Normalize("/a/b")), Is.True);
        });
    }
}
