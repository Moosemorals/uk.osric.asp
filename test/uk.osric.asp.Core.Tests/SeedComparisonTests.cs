// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class SeedComparisonTests {

    [TestCase("s", "s", "s", SeedVerdict.InSync, false, false)]
    [TestCase("r", "s", "r", SeedVerdict.ReleaseNewer, true, false)]
    [TestCase("s", "s", "h", SeedVerdict.Edited, false, false)]
    [TestCase("r", "s", "h", SeedVerdict.Conflict, false, false)]
    [TestCase("r", "s", "s", SeedVerdict.StoreNewer, false, true)]
    [TestCase(null, "s", "h", SeedVerdict.Unknown, false, false)]
    [TestCase(null, "s", "s", SeedVerdict.Unknown, false, true)]
    [TestCase("r", null, "h", SeedVerdict.Retired, false, false)]
    public void TheComparisonSaysWhatTheRowIs(
        string? recorded,
        string? shipped,
        string? content,
        SeedVerdict verdict,
        bool canApply,
        bool canAdopt) {
        SeedComparison comparison = SeedComparison.Of(recorded, shipped, content);

        Assert.Multiple(() => {
            Assert.That(comparison.Verdict, Is.EqualTo(verdict));
            Assert.That(comparison.CanApply, Is.EqualTo(canApply));
            Assert.That(comparison.CanAdopt, Is.EqualTo(canAdopt));
        });
    }

    [Test]
    public void OnlyARowThatHoldsTheInstalledSeedMayBeApplied() {
        Assert.Multiple(() => {
            Assert.That(SeedComparison.Of("r", "s", "r").CanApply, Is.True);
            Assert.That(SeedComparison.Of("s", "s", "h").CanApply, Is.False);
            Assert.That(SeedComparison.Of("r", "s", "h").CanApply, Is.False);
        });
    }

    [Test]
    public void AdoptionNeedsTheContentToBeExactlyWhatTheReleaseShips() {
        Assert.Multiple(() => {
            Assert.That(SeedComparison.Of("r", "s", "s").CanAdopt, Is.True);
            Assert.That(SeedComparison.Of(null, "s", "s").CanAdopt, Is.True);
            Assert.That(SeedComparison.Of("r", "s", "r").CanAdopt, Is.False);
            Assert.That(SeedComparison.Of("s", "s", "s").CanAdopt, Is.False, "already recorded");
        });
    }
}
