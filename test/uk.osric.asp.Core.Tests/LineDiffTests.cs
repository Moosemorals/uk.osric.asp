// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Diff;

[TestFixture]
public sealed class LineDiffTests {

    [Test]
    public void IdenticalDocumentsHaveNoHunks() {
        DiffDocument diff = LineDiff.Compare("one\ntwo\nthree", "one\ntwo\nthree");

        Assert.Multiple(() => {
            Assert.That(diff.IsEmpty, Is.True);
            Assert.That(diff.Hunks, Is.Empty);
            Assert.That(diff.AddedLines, Is.Zero);
            Assert.That(diff.RemovedLines, Is.Zero);
            Assert.That(diff.IsCoarse, Is.False);
        });
    }

    [Test]
    public void AnEmptyPairHasNoHunks() {
        Assert.That(LineDiff.Compare("", "").IsEmpty, Is.True);
    }

    [Test]
    public void AnAdditionIsOneAddedLineWithContext() {
        DiffDocument diff = LineDiff.Compare("one\nthree", "one\ntwo\nthree");

        Assert.Multiple(() => {
            Assert.That(diff.IsEmpty, Is.False);
            Assert.That(diff.AddedLines, Is.EqualTo(1));
            Assert.That(diff.RemovedLines, Is.Zero);
            Assert.That(diff.Hunks, Has.Count.EqualTo(1));
            Assert.That(diff.Hunks[0].Lines.Select(line => line.Kind),
                Is.EqualTo([DiffLineKind.Context, DiffLineKind.Added, DiffLineKind.Context]));
            Assert.That(diff.Hunks[0].Lines[1].Text, Is.EqualTo("two"));
            Assert.That(diff.Hunks[0].Lines[1].BeforeLine, Is.Null);
            Assert.That(diff.Hunks[0].Lines[1].AfterLine, Is.EqualTo(2));
        });
    }

    [Test]
    public void ARemovalIsOneRemovedLineWithContext() {
        DiffDocument diff = LineDiff.Compare("one\ntwo\nthree", "one\nthree");

        Assert.Multiple(() => {
            Assert.That(diff.AddedLines, Is.Zero);
            Assert.That(diff.RemovedLines, Is.EqualTo(1));
            Assert.That(diff.Hunks[0].Lines.Select(line => line.Kind),
                Is.EqualTo([DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Context]));
            Assert.That(diff.Hunks[0].Lines[1].BeforeLine, Is.EqualTo(2));
            Assert.That(diff.Hunks[0].Lines[1].AfterLine, Is.Null);
        });
    }

    [Test]
    public void AReplacementIsARemovedLineThenAnAddedLine() {
        DiffDocument diff = LineDiff.Compare("one\ntwo\nthree", "one\nTWO\nthree");

        Assert.Multiple(() => {
            Assert.That(diff.AddedLines, Is.EqualTo(1));
            Assert.That(diff.RemovedLines, Is.EqualTo(1));
            Assert.That(diff.Hunks[0].Lines.Select(line => line.Kind),
                Is.EqualTo([DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added, DiffLineKind.Context]));
            Assert.That(diff.Hunks[0].Lines[1].Text, Is.EqualTo("two"));
            Assert.That(diff.Hunks[0].Lines[2].Text, Is.EqualTo("TWO"));
        });
    }

    [Test]
    public void LineNumbersSurviveTheTrimmedPrefixAndSuffix() {
        DiffDocument diff = LineDiff.Compare(
            "one\ntwo\nthree\nfour\nfive",
            "one\ntwo\nTHREE\nfour\nfive");

        DiffLine removed = diff.Hunks[0].Lines.Single(line => line.Kind == DiffLineKind.Removed);
        DiffLine added = diff.Hunks[0].Lines.Single(line => line.Kind == DiffLineKind.Added);

        Assert.Multiple(() => {
            Assert.That(removed.BeforeLine, Is.EqualTo(3));
            Assert.That(removed.AfterLine, Is.Null);
            Assert.That(added.BeforeLine, Is.Null);
            Assert.That(added.AfterLine, Is.EqualTo(3));
        });
    }

    [Test]
    public void CrlfAndLfLinesCompareEqual() {
        DiffDocument diff = LineDiff.Compare("one\r\ntwo\r\nthree", "one\ntwo\nthree");

        Assert.That(diff.IsEmpty, Is.True);
    }

    [Test]
    public void AFinishingNewlineDoesNotCreateAnExtraLine() {
        DiffDocument diff = LineDiff.Compare("one\ntwo\n", "one\ntwo");

        Assert.That(diff.IsEmpty, Is.True);
    }

    [Test]
    public void ABlankLineIsALine() {
        DiffDocument diff = LineDiff.Compare("one\n\ntwo", "one\ntwo");

        Assert.Multiple(() => {
            Assert.That(diff.RemovedLines, Is.EqualTo(1));
            Assert.That(diff.Hunks[0].Lines.Single(line => line.Kind == DiffLineKind.Removed).Text, Is.Empty);
        });
    }

    [Test]
    public void TabsAndNonAsciiTextArePreserved() {
        DiffDocument diff = LineDiff.Compare("\té", "\tü");

        Assert.Multiple(() => {
            Assert.That(diff.Hunks[0].Lines[0].Text, Is.EqualTo("\té"));
            Assert.That(diff.Hunks[0].Lines[1].Text, Is.EqualTo("\tü"));
        });
    }

    [Test]
    public void ChangesSixLinesApartShareAHunk() {
        string before = string.Join('\n', Enumerable.Range(1, 20));
        string[] lines = before.Split('\n');
        lines[2] = "changed three";
        lines[9] = "changed ten";

        DiffDocument diff = LineDiff.Compare(before, string.Join('\n', lines));

        Assert.Multiple(() => {
            Assert.That(diff.Hunks, Has.Count.EqualTo(1));
            Assert.That(diff.AddedLines, Is.EqualTo(2));
            Assert.That(diff.RemovedLines, Is.EqualTo(2));
        });
    }

    [Test]
    public void ChangesSevenLinesApartAreTwoHunks() {
        string before = string.Join('\n', Enumerable.Range(1, 20));
        string[] lines = before.Split('\n');
        lines[2] = "changed three";
        lines[10] = "changed eleven";

        DiffDocument diff = LineDiff.Compare(before, string.Join('\n', lines));

        Assert.Multiple(() => {
            Assert.That(diff.Hunks, Has.Count.EqualTo(2));
            Assert.That(diff.Hunks[0].BeforeStart, Is.EqualTo(1));
            Assert.That(diff.Hunks[0].BeforeCount, Is.EqualTo(6));
            Assert.That(diff.Hunks[1].BeforeStart, Is.EqualTo(8));
            Assert.That(diff.Hunks[1].BeforeCount, Is.EqualTo(7));
        });
    }

    [Test]
    public void AChangeAtTheEndKeepsItsTrailingContextToTheLastLine() {
        DiffDocument diff = LineDiff.Compare("one\ntwo\nthree", "one\ntwo\nTHREE");

        Assert.Multiple(() => {
            Assert.That(diff.Hunks, Has.Count.EqualTo(1));
            Assert.That(diff.Hunks[0].BeforeStart, Is.EqualTo(1));
            Assert.That(diff.Hunks[0].BeforeCount, Is.EqualTo(3));
            Assert.That(diff.Hunks[0].AfterCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void AnInsertionIntoAnEmptyDocumentHasAZeroBeforeRange() {
        DiffDocument diff = LineDiff.Compare("", "one\ntwo");

        Assert.Multiple(() => {
            Assert.That(diff.Hunks, Has.Count.EqualTo(1));
            Assert.That(diff.Hunks[0].BeforeStart, Is.Zero);
            Assert.That(diff.Hunks[0].BeforeCount, Is.Zero);
            Assert.That(diff.Hunks[0].AfterStart, Is.EqualTo(1));
            Assert.That(diff.Hunks[0].AfterCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void TooManyEditsFallsBackToACoarseReplacement() {
        string before = string.Join('\n', Enumerable.Range(0, LineDiff.MaxEditDistance + 1).Select(index => $"a{index}"));
        string after = string.Join('\n', Enumerable.Range(0, LineDiff.MaxEditDistance + 1).Select(index => $"b{index}"));

        DiffDocument diff = LineDiff.Compare(before, after);

        Assert.Multiple(() => {
            Assert.That(diff.IsCoarse, Is.True);
            Assert.That(diff.RemovedLines, Is.EqualTo(LineDiff.MaxEditDistance + 1));
            Assert.That(diff.AddedLines, Is.EqualTo(LineDiff.MaxEditDistance + 1));
            Assert.That(diff.Hunks, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void TooManyLinesFallsBackToACoarseReplacement() {
        int lines = (LineDiff.MaxLines / 2) + 1;
        string before = string.Join('\n', Enumerable.Repeat("a", lines));
        string after = string.Join('\n', Enumerable.Repeat("b", lines));

        DiffDocument diff = LineDiff.Compare(before, after);

        Assert.Multiple(() => {
            Assert.That(diff.IsCoarse, Is.True);
            Assert.That(diff.RemovedLines, Is.EqualTo(lines));
            Assert.That(diff.AddedLines, Is.EqualTo(lines));
        });
    }

    [Test]
    public void ANullDocumentIsRefused() {
        Assert.Multiple(() => {
            Assert.Throws<ArgumentNullException>(() => LineDiff.Compare(null!, "one"));
            Assert.Throws<ArgumentNullException>(() => LineDiff.Compare("one", null!));
        });
    }

    [Test]
    public void EveryHunkCoversTheLinesItSaysItCovers() {
        Random random = new(20261110);
        string[] alphabet = ["a", "b", "c", "d", "e"];

        for (int iteration = 0; iteration < 300; iteration++) {
            string[] beforeLines = [.. Enumerable.Range(0, random.Next(0, 40)).Select(_ => alphabet[random.Next(alphabet.Length)])];
            string[] afterLines = [.. Enumerable.Range(0, random.Next(0, 40)).Select(_ => alphabet[random.Next(alphabet.Length)])];
            DiffDocument diff = LineDiff.Compare(string.Join('\n', beforeLines), string.Join('\n', afterLines));

            if (diff.IsCoarse) {
                continue;
            }

            Assert.Multiple(() => {
                Assert.That(diff.AddedLines,
                    Is.EqualTo(diff.Hunks.Sum(hunk => hunk.Lines.Count(line => line.Kind == DiffLineKind.Added))));
                Assert.That(diff.RemovedLines,
                    Is.EqualTo(diff.Hunks.Sum(hunk => hunk.Lines.Count(line => line.Kind == DiffLineKind.Removed))));
            });

            foreach (DiffHunk hunk in diff.Hunks) {
                string[] hunkBefore = [.. hunk.Lines
                    .Where(line => line.Kind != DiffLineKind.Added)
                    .Select(line => line.Text)];
                string[] hunkAfter = [.. hunk.Lines
                    .Where(line => line.Kind != DiffLineKind.Removed)
                    .Select(line => line.Text)];

                if (hunk.BeforeCount == 0) {
                    Assert.Multiple(() => {
                        Assert.That(hunkBefore, Is.Empty);
                        Assert.That(hunk.BeforeStart, Is.Zero);
                    });
                } else {
                    Assert.That(
                        hunkBefore,
                        Is.EqualTo(beforeLines[(hunk.BeforeStart - 1)..(hunk.BeforeStart - 1 + hunk.BeforeCount)]),
                        $"iteration {iteration}: before range");
                }

                if (hunk.AfterCount == 0) {
                    Assert.Multiple(() => {
                        Assert.That(hunkAfter, Is.Empty);
                        Assert.That(hunk.AfterStart, Is.Zero);
                    });
                } else {
                    Assert.That(
                        hunkAfter,
                        Is.EqualTo(afterLines[(hunk.AfterStart - 1)..(hunk.AfterStart - 1 + hunk.AfterCount)]),
                        $"iteration {iteration}: after range");
                }
            }
        }
    }
}
