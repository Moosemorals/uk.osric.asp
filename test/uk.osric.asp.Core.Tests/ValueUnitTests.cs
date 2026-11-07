// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Charting;

[TestFixture]
public sealed class ValueUnitTests {

    [Test]
    public void ASubSecondRangeIsDrawnInMilliseconds() {
        ChartScale scale = ValueUnit.ForSeconds(0.00025, 0.000475);

        Assert.That(scale.Min, Is.EqualTo(0.25).Within(1e-12));
        Assert.That(scale.Max, Is.EqualTo(0.475).Within(1e-12));
        Assert.That(scale.Unit, Is.EqualTo("milliseconds"));
    }

    [Test]
    public void ARangeThatReachesASecondStaysInSeconds() {
        ChartScale scale = ValueUnit.ForSeconds(0.2, 1);

        Assert.That(scale.Min, Is.EqualTo(0.2));
        Assert.That(scale.Max, Is.EqualTo(1));
        Assert.That(scale.Unit, Is.EqualTo("seconds"));
    }

    [Test]
    public void AByteRangeThatReachesAMegabyteIsDrawnInMegabytes() {
        ChartScale scale = ValueUnit.ForBytes(1_000_000, 2_000_000);

        Assert.That(scale.Unit, Is.EqualTo("megabytes"));
        Assert.That(scale.Min, Is.EqualTo(1));
        Assert.That(scale.Max, Is.EqualTo(2));
    }

    [Test]
    public void AByteRangeThatReachesAKilobyteIsDrawnInKilobytes() {
        ChartScale scale = ValueUnit.ForBytes(1_000, 2_000);

        Assert.That(scale.Unit, Is.EqualTo("kilobytes"));
        Assert.That(scale.Min, Is.EqualTo(1));
        Assert.That(scale.Max, Is.EqualTo(2));
    }

    [Test]
    public void AByteRangeOverAMegabyteIsDrawnInMegabytes() {
        ChartScale scale = ValueUnit.ForBytes(166_000_000, 180_000_000);

        Assert.That(scale.Unit, Is.EqualTo("megabytes"));
        Assert.That(scale.Max, Is.EqualTo(180).Within(1e-9));
    }

    [Test]
    public void AByteRangeUnderAMegabyteIsDrawnInKilobytes() {
        ChartScale scale = ValueUnit.ForBytes(1024, 65536);

        Assert.That(scale.Unit, Is.EqualTo("kilobytes"));
        Assert.That(scale.Max, Is.EqualTo(65.536).Within(1e-9));
    }

    [Test]
    public void ASmallByteRangeStaysInBytes() {
        ChartScale scale = ValueUnit.ForBytes(10, 900);

        Assert.That(scale.Unit, Is.EqualTo("bytes"));
    }

    [Test]
    public void ALongRangeStaysInSeconds() {
        ChartScale scale = ValueUnit.ForSeconds(1.2, 3.4);

        Assert.That(scale.Unit, Is.EqualTo("seconds"));
        Assert.That(scale.Max, Is.EqualTo(3.4));
    }
}
