// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class AccountBalanceAmountTests {

    [TestCaseSource(nameof(Figures))]
    public void ReadsAFigureTheProviderSent(string total, decimal expected) {
        bool read = AccountBalanceAmounts.TryRead(total, out decimal amount);

        Assert.Multiple(() => {
            Assert.That(read, Is.True);
            Assert.That(amount, Is.EqualTo(expected));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("abc")]
    [TestCase("110.00 CR")]
    public void RefusesTextThatIsNotAFigure(string total) {
        Assert.That(AccountBalanceAmounts.TryRead(total, out _), Is.False);
    }

    private static IEnumerable<TestCaseData> Figures() {
        yield return new TestCaseData("110.00", 110.00m).SetName("ReadsAWholeFigure");
        yield return new TestCaseData("0.01", 0.01m).SetName("ReadsATrailingFigure");
        yield return new TestCaseData("-1.25", -1.25m).SetName("ReadsANegativeFigure");
        yield return new TestCaseData(" 42.50 ", 42.50m).SetName("ReadsAFigureWithSurroundingSpace");
    }
}
