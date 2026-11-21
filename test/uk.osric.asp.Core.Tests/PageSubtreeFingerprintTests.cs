// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

/// <summary>The fingerprint a listing carries of every head under its prefix.</summary>
[TestFixture]
public sealed class PageSubtreeFingerprintTests {

    [Test]
    public void AnEmptySetAnswersTheTokenOfAnEmptySetAndAnswersItAgain() {
        string first = PageSubtreeFingerprint.Of([]);

        Assert.That(PageSubtreeFingerprint.Of([]), Is.EqualTo(first));
    }

    [Test]
    public void TheSameFactsInAnotherOrderAnswerTheSameToken() {
        PageFingerprintFact one = Fact("/one", "revision-one");
        PageFingerprintFact two = Fact("/two", "revision-two");

        Assert.That(PageSubtreeFingerprint.Of([two, one]), Is.EqualTo(PageSubtreeFingerprint.Of([one, two])));
    }

    [Test]
    public void ATombstoneMovesTheToken() {
        PageFingerprintFact live = Fact("/blog", "revision-one");
        PageFingerprintFact deleted = live with { IsDeleted = true };

        Assert.That(PageSubtreeFingerprint.Of([deleted]), Is.Not.EqualTo(PageSubtreeFingerprint.Of([live])));
    }

    [Test]
    public void AChangedRevisionMovesTheToken() {
        PageFingerprintFact before = Fact("/blog", "revision-one");
        PageFingerprintFact after = Fact("/blog", "revision-two");

        Assert.That(PageSubtreeFingerprint.Of([after]), Is.Not.EqualTo(PageSubtreeFingerprint.Of([before])));
    }

    [Test]
    public void AChangedSeedAnchorMovesTheToken() {
        PageFingerprintFact before = Fact("/blog", "revision-one") with { SeedHash = "seed-one" };
        PageFingerprintFact after = Fact("/blog", "revision-one") with { SeedHash = "seed-two" };

        Assert.That(PageSubtreeFingerprint.Of([after]), Is.Not.EqualTo(PageSubtreeFingerprint.Of([before])));
    }

    [Test]
    public void AChangedContentHashMovesTheToken() {
        PageFingerprintFact before = Fact("/blog", "revision-one");
        PageFingerprintFact after = Fact("/blog", "revision-one") with { ContentHash = "content-two" };

        Assert.That(PageSubtreeFingerprint.Of([after]), Is.Not.EqualTo(PageSubtreeFingerprint.Of([before])));
    }

    [Test]
    public void AKeyCannotRunIntoTheFieldAfterIt() {
        PageFingerprintFact joined = Fact("/a", "bc");
        PageFingerprintFact split = Fact("/ab", "c");

        Assert.That(PageSubtreeFingerprint.Of([split]), Is.Not.EqualTo(PageSubtreeFingerprint.Of([joined])));
    }

    [Test]
    public void ANullSetIsRefused() {
        Assert.Throws<ArgumentNullException>(() => PageSubtreeFingerprint.Of(null!));
    }

    private static PageFingerprintFact Fact(string key, string revisionId) =>
        new(key, revisionId, IsDeleted: false, SeedHash: null, ContentHash: "content-one");
}
