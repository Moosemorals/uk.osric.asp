// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Diff;

[TestFixture]
public sealed class NormalDiffTests {

    [Test]
    public void AChangedLineIsWrittenAsAChangeBlock() {
        string text = LineDiff.Compare("one\ntwo\nthree", "one\nTWO\nthree").ToNormal();

        Assert.That(text, Is.EqualTo("2c2\n< two\n---\n> TWO\n"));
    }

    [Test]
    public void AddedLinesSayWhichLineTheyFollow() {
        string text = LineDiff.Compare("one", "one\ntwo\nthree").ToNormal();

        Assert.That(text, Is.EqualTo("1a2,3\n> two\n> three\n"));
    }

    [Test]
    public void DeletedLinesSayWhichLineTheyFollowed() {
        string text = LineDiff.Compare("zero\none\ntwo", "zero").ToNormal();

        Assert.That(text, Is.EqualTo("2,3d1\n< one\n< two\n"));
    }

    [Test]
    public void ADeletionBeforeTheFirstLineFollowsLineZero() {
        string text = LineDiff.Compare("one\ntwo", "two").ToNormal();

        Assert.That(text, Is.EqualTo("1d0\n< one\n"));
    }

    [Test]
    public void AChangeAtTheStartOfTheDocumentIsOneBlock() {
        string text = LineDiff.Compare("one\ntwo", "ONE\ntwo").ToNormal();

        Assert.That(text, Is.EqualTo("1c1\n< one\n---\n> ONE\n"));
    }

    [Test]
    public void ChangesFarApartAreTwoBlocksAndCloseOnesAreOne() {
        string far = LineDiff.Compare(
            string.Join('\n', Enumerable.Range(1, 12).Select(line => $"line {line}")),
            string.Join('\n', Enumerable.Range(1, 12).Select(line => line is 2 or 11 ? $"changed {line}" : $"line {line}"))).ToNormal();
        string close = LineDiff.Compare("a\nb\nc", "A\nb\nC").ToNormal();

        Assert.Multiple(() => {
            Assert.That(far, Does.Contain("2c2").And.Contain("11c11"), "a block per change, far apart");
            Assert.That(close, Does.Contain("1c1"), "each change still gets its own block");
        });
    }

    [Test]
    public void TheOutputEndsWithALineBreak() {
        string text = LineDiff.Compare("one", "two").ToNormal();

        Assert.That(text, Does.EndWith("\n"), "as a diff file does, so the executor trims it");
    }

    [Test]
    public void IdenticalDocumentsWriteNothing() {
        Assert.That(LineDiff.Compare("one\ntwo", "one\ntwo").ToNormal(), Is.Empty);
    }
}
