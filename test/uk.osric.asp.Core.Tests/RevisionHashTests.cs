// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageRevisionHashTests {

    [Test]
    public void SameRevisionInputsProduceAStableLowercaseSha256Address() {
        DateTimeOffset savedAt = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);

        string first = RevisionHash.Of("<p>caf\u00e9</p>", "runtime:1", savedAt, "revision-a");
        string second = RevisionHash.Of("<p>caf\u00e9</p>", "runtime:1", savedAt, "revision-a");

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(RevisionHash.FormatVersion, Is.EqualTo("page-revision-v1"));
        Assert.That(first, Is.EqualTo("554b6dcaafb139460670372285dd69a3b0bbd8264f5292f15279d7786a6869d7"));
    }

    [Test]
    public void EveryIdentityFieldContributesToTheAddress() {
        DateTimeOffset savedAt = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);
        string baseline = RevisionHash.Of("a", "bc", savedAt, "id");

        Assert.Multiple(() => {
            Assert.That(RevisionHash.Of("b", "bc", savedAt, "id"), Is.Not.EqualTo(baseline));
            Assert.That(RevisionHash.Of("a", "c", savedAt, "id"), Is.Not.EqualTo(baseline));
            Assert.That(RevisionHash.Of("a", "bc", savedAt.AddTicks(1), "id"), Is.Not.EqualTo(baseline));
            Assert.That(RevisionHash.Of("a", "bc", savedAt, "other"), Is.Not.EqualTo(baseline));
        });
    }

    [Test]
    public void TupleFieldsAreLengthFramedRatherThanConcatenated() {
        DateTimeOffset savedAt = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);

        string first = RevisionHash.Of("a", "bc", savedAt, "id");
        string second = RevisionHash.Of("ab", "c", savedAt, "id");

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void NullComponentsAreNamedInTheRejection() {
        DateTimeOffset savedAt = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);

        Assert.Multiple(() => {
            Assert.That(() => RevisionHash.Of(null!, "runtime", savedAt, "id"),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("source"));
            Assert.That(() => RevisionHash.Of("source", null!, savedAt, "id"),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("creationRuntimeId"));
            Assert.That(() => RevisionHash.Of("source", "runtime", savedAt, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("revisionId"));
        });
    }

    [Test]
    public void EquivalentUtcInstantsHaveTheSameTimestampEncoding() {
        DateTimeOffset utc = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);
        DateTimeOffset offset = utc.ToOffset(TimeSpan.FromHours(5));

        Assert.That(RevisionHash.Of("source", "runtime", offset, "id"),
            Is.EqualTo(RevisionHash.Of("source", "runtime", utc, "id")));
    }

    [Test]
    public void AGeneratedHashIsCanonical() {
        string hash = RevisionHash.Of("source", "runtime", DateTimeOffset.UnixEpoch, "id");

        Assert.That(RevisionHash.Length, Is.EqualTo(64));
        Assert.That(RevisionHash.IsCanonical(hash), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")] // 63
    [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")] // 65
    [TestCase("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    [TestCase(" 123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void AnythingButTheStoredSpellingIsNotCanonical(string? hash) {
        Assert.That(RevisionHash.IsCanonical(hash), Is.False);
    }
}
