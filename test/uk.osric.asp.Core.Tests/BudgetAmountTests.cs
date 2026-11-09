// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class BudgetAmountTests {

    [TestCase(0, "0")]
    [TestCase(5, "5")]
    [TestCase(10, "10")]
    [TestCase(100, "100")]
    [TestCase(600, "600")]
    [TestCase(999, "999")]
    [TestCase(1_000, "1k")]
    [TestCase(1_249, "1.2k")]
    [TestCase(9_949, "9.9k")]
    [TestCase(9_950, "10k")]
    [TestCase(16_384, "16k")]
    [TestCase(500_000, "500k")]
    [TestCase(999_999, "1M")]
    [TestCase(1_234_567, "1.2M")]
    [TestCase(1_000_000_000, "1G")]
    [TestCase(1_000_000_000_000, "1T")]
    public void AFigureIsAtMostThreeDigitsAndAUnitLetter(long value, string expected) {
        Assert.That(BudgetAmount.Format(value), Is.EqualTo(expected));
    }

    [Test]
    public void ANegativeFigureIsRejected() {
        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetAmount.Format(-1));
    }
}
