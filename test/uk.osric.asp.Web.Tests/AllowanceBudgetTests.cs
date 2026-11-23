// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Pages;

/// <summary>Proves each budget is a rate the render's clock refills, not a count it spends once.</summary>
[TestFixture]
public sealed class AllowanceBudgetTests {

    [Test]
    public void AFetchBudgetRefillsAtItsRate() {
        TestClock clock = new();
        FetchBudget budget = new(capacity: 1, perSecond: 2, clock, PageFetchOptions.RateKey);

        Assert.Multiple(() => {
            Assert.That(budget.TryTake(), Is.True);
            Assert.That(budget.TryTake(), Is.False, "the burst is all it had");
            Assert.That(budget.Available, Is.Zero);
        });

        clock.Now = clock.Now.AddSeconds(0.5);

        Assert.Multiple(() => {
            Assert.That(budget.Available, Is.EqualTo(1));
            Assert.That(budget.TryTake(), Is.True);
            Assert.That(budget.TryTake(), Is.False, "taking the refilled unit did not carry a second");
        });
    }

    [Test]
    public void AReadBudgetRefillsAtItsRate() {
        TestClock clock = new();
        ReadBudget budget = new(capacity: 10, perSecond: 20, clock, PageReadOptions.RateKey);

        Assert.Multiple(() => {
            Assert.That(budget.TryCharge(10), Is.True);
            Assert.That(budget.TryCharge(1), Is.False);
        });

        clock.Now = clock.Now.AddSeconds(0.05);

        Assert.Multiple(() => {
            Assert.That(budget.TryCharge(1), Is.True, "twenty characters per second refills one in fifty milliseconds");
            Assert.That(budget.TryCharge(1), Is.False);
        });
    }

    [Test]
    public void ABudgetDescribesItsOwnRefusal() {
        FetchBudget fetch = new(capacity: 10, perSecond: 2, TimeProvider.System, PageFetchOptions.RateKey);
        ReadBudget read = new(capacity: 524_290, perSecond: 104_858, TimeProvider.System, PageReadOptions.RateKey);

        Assert.Multiple(() => {
            Assert.That(fetch.DescribeRefusal(), Does.Contain("2 fetches per second").And.Contain(PageFetchOptions.RateKey)
                .And.Contain("10 at most in one burst"));
            Assert.That(read.DescribeRefusal(), Does.Contain("104858 characters of page source per second")
                .And.Contain(PageReadOptions.RateKey).And.Contain("524290 at most in one burst"));
        });
    }
}
