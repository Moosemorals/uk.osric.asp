// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageRevisionHashTests {

    [Test]
    public void SameRevisionInputsProduceAStableLowercaseSha256Address() {
        DateTimeOffset savedAt = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);

        string first = PageRevisionHash.Compute("<p>caf\u00e9</p>", "runtime:1", savedAt, "revision-a");
        string second = PageRevisionHash.Compute("<p>caf\u00e9</p>", "runtime:1", savedAt, "revision-a");

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Does.Match("^[0-9a-f]{64}$"));
        Assert.That(PageRevisionHash.FormatVersion, Is.EqualTo("page-revision-v1"));
        Assert.That(first, Is.EqualTo("554b6dcaafb139460670372285dd69a3b0bbd8264f5292f15279d7786a6869d7"));
    }

    [Test]
    public void EveryIdentityFieldContributesToTheAddress() {
        DateTimeOffset savedAt = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);
        string baseline = PageRevisionHash.Compute("a", "bc", savedAt, "id");

        Assert.Multiple(() => {
            Assert.That(PageRevisionHash.Compute("b", "bc", savedAt, "id"), Is.Not.EqualTo(baseline));
            Assert.That(PageRevisionHash.Compute("a", "c", savedAt, "id"), Is.Not.EqualTo(baseline));
            Assert.That(PageRevisionHash.Compute("a", "bc", savedAt.AddTicks(1), "id"), Is.Not.EqualTo(baseline));
            Assert.That(PageRevisionHash.Compute("a", "bc", savedAt, "other"), Is.Not.EqualTo(baseline));
        });
    }

    [Test]
    public void TupleFieldsAreLengthFramedRatherThanConcatenated() {
        DateTimeOffset savedAt = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);

        string first = PageRevisionHash.Compute("a", "bc", savedAt, "id");
        string second = PageRevisionHash.Compute("ab", "c", savedAt, "id");

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void EquivalentUtcInstantsHaveTheSameTimestampEncoding() {
        DateTimeOffset utc = new(2026, 11, 3, 9, 15, 30, TimeSpan.Zero);
        DateTimeOffset offset = utc.ToOffset(TimeSpan.FromHours(5));

        Assert.That(PageRevisionHash.Compute("source", "runtime", offset, "id"),
            Is.EqualTo(PageRevisionHash.Compute("source", "runtime", utc, "id")));
    }
}
