// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;

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
    public void ALongRangeStaysInSeconds() {
        ChartScale scale = ValueUnit.ForSeconds(1.2, 3.4);

        Assert.That(scale.Unit, Is.EqualTo("seconds"));
        Assert.That(scale.Max, Is.EqualTo(3.4));
    }
}
