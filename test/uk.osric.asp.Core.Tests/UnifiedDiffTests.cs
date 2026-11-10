// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Diff;

[TestFixture]
public sealed class UnifiedDiffTests {

    [Test]
    public void AReplacementIsAHeaderAndPrefixedLines() {
        string text = UnifiedDiff.Format(LineDiff.Compare("one\ntwo\nthree", "one\nTWO\nthree"));

        Assert.That(text, Is.EqualTo("@@ -1,3 +1,3 @@\n one\n-two\n+TWO\n three\n"));
    }

    [Test]
    public void AnInsertionAtTheStartHasTheRightHeader() {
        string text = UnifiedDiff.Format(LineDiff.Compare("b", "a\nb"));

        Assert.That(text, Is.EqualTo("@@ -1,1 +1,2 @@\n+a\n b\n"));
    }

    [Test]
    public void ADocumentThatBecomesEmptyHasAZeroAfterRange() {
        string text = UnifiedDiff.Format(LineDiff.Compare("a", ""));

        Assert.That(text, Is.EqualTo("@@ -1,1 +0,0 @@\n-a\n"));
    }

    [Test]
    public void AnEmptyDocumentThatGainsLinesHasAZeroBeforeRange() {
        string text = UnifiedDiff.Format(LineDiff.Compare("", "a\nb"));

        Assert.That(text, Is.EqualTo("@@ -0,0 +1,2 @@\n+a\n+b\n"));
    }

    [Test]
    public void NoChangesWriteNothing() {
        Assert.That(UnifiedDiff.Format(LineDiff.Compare("one", "one")), Is.Empty);
    }

    [Test]
    public void ANullDocumentIsRefused() {
        Assert.Throws<ArgumentNullException>(() => UnifiedDiff.Format(null!));
    }
}
