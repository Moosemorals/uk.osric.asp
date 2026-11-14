// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Diff;

[TestFixture]
public sealed class DiffOptionTests {

    [Test]
    public void ContextIsThreeLinesUnlessAskedForMore() {
        string before = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine";
        string after = "one\ntwo\nthree\nfour\nFIVE\nsix\nseven\neight\nnine";

        DiffDocument stock = LineDiff.Compare(before, after);
        DiffDocument none = LineDiff.Compare(before, after, 0, ignoreSpaceChange: false);
        DiffDocument wide = LineDiff.Compare(before, after, 5, ignoreSpaceChange: false);

        Assert.Multiple(() => {
            Assert.That(stock.Hunks[0].Lines, Has.Count.EqualTo(8), "three lines either side of the change");
            Assert.That(none.Hunks[0].Lines, Has.Count.EqualTo(2), "the removed and the added line alone");
            Assert.That(wide.Hunks[0].Lines, Has.Count.EqualTo(10), "five lines either side, to the ends");
        });
    }

    [Test]
    public void AChangeInTrailingWhitespaceIsNotAChangeWhenItIsIgnored() {
        string before = "one\ntwo   \nthree";
        string after = "one\ntwo\nthree";

        Assert.Multiple(() => {
            Assert.That(LineDiff.Compare(before, after).IsEmpty, Is.False, "whitespace is a difference by default");
            Assert.That(LineDiff.Compare(before, after, 3, ignoreSpaceChange: true).IsEmpty, Is.True);
        });
    }

    [Test]
    public void AChangeInTheAmountOfWhitespaceIsNotAChangeWhenItIsIgnored() {
        string before = "one\ttwo\tthree";
        string after = "one two three";

        Assert.That(LineDiff.Compare(before, after, 3, ignoreSpaceChange: true).IsEmpty, Is.True);
    }

    [Test]
    public void TheIgnoredWhitespaceIsIgnoredAndTheLineIsNot() {
        // The comparison runs on a collapsed copy, so the text that comes back is the text as written.
        DiffDocument diff = LineDiff.Compare("one   \nOLD\tline\nthree", "one\nNEW line\nthree", 3, ignoreSpaceChange: true);

        Assert.Multiple(() => {
            Assert.That(diff.Hunks, Has.Count.EqualTo(1));
            Assert.That(diff.Hunks[0].Lines.Select(line => line.Kind),
                Is.EqualTo([DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added, DiffLineKind.Context]));
            Assert.That(diff.Hunks[0].Lines[0].Text, Is.EqualTo("one   "), "a context line keeps its trailing blanks");
            Assert.That(diff.Hunks[0].Lines[1].Text, Is.EqualTo("OLD\tline"));
            Assert.That(diff.Hunks[0].Lines[2].Text, Is.EqualTo("NEW line"));
        });
    }

    [Test]
    public void NegativeContextIsRefused() {
        Assert.That(() => LineDiff.Compare("one", "two", -1, ignoreSpaceChange: false),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("contextLines"));
    }
}
