// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public class PageSourceChangeTests {

    [Test]
    public void ANewOrDeletedPageIsAlwaysAChange() {
        Assert.That(PageSourceChange.IsNoOp(null, "text"), Is.False);
    }

    [Test]
    public void TheSourceThePageHoldsIsNoChange() {
        Assert.That(PageSourceChange.IsNoOp("text", "text"), Is.True);
    }

    [Test]
    public void DifferentSourceIsAChange() {
        Assert.That(PageSourceChange.IsNoOp("text", "other"), Is.False);
    }

    [Test]
    public void TheComparisonIsOrdinal() {
        Assert.Multiple(() => {
            Assert.That(PageSourceChange.IsNoOp("Text", "text"), Is.False);
            Assert.That(PageSourceChange.IsNoOp("text\r\n", "text\n"), Is.False);
        });
    }
}
