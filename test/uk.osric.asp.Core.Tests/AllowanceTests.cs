// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

/// <summary>Proves an allowance is a rate with a burst, and that the clock is the caller's.</summary>
[TestFixture]
public sealed class AllowanceTests {

    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Test]
    public void AFreshAllowanceIsFull() {
        Allowance allowance = new(capacity: 10, perSecond: 2, Start);
        List<bool> takes = [];

        for (int index = 0; index < 11; index++) {
            Assert.That(allowance.TryTake(1, Start, out allowance), Is.EqualTo(index < 10));
            takes.Add(index < 10);
        }

        Assert.That(takes, Is.EqualTo([.. Enumerable.Repeat(true, 10), false]),
            "the burst is what the first instant may spend, and the eleventh is refused");
    }

    [Test]
    public void ItRefillsAtItsRate() {
        Allowance allowance = new(capacity: 10, perSecond: 2, Start);
        Assert.That(allowance.TryTake(10, Start, out allowance), Is.True);

        Assert.Multiple(() => {
            Assert.That(allowance.TryTake(1, Start.AddSeconds(0.25), out _), Is.False,
                "a quarter second at two per second is half a unit, which is not a whole one");
            Assert.That(allowance.TryTake(1, Start.AddSeconds(0.5), out Allowance after), Is.True);
            Assert.That(after.Available(Start.AddSeconds(0.5)), Is.Zero);
        });
    }

    [Test]
    public void ARefusalCostsNothing() {
        Allowance allowance = new(capacity: 1, perSecond: 0.001, Start);
        Assert.That(allowance.TryTake(1, Start, out allowance), Is.True);

        Assert.Multiple(() => {
            Assert.That(allowance.TryTake(1, Start, out Allowance refused), Is.False);
            Assert.That(refused.Available(Start), Is.Zero, "the refusal left the allowance where it was");
            Assert.That(refused.TryTake(1, Start.AddSeconds(1_000), out _), Is.True,
                "the refused instant is not recorded, so the next refill is measured from the take");
        });
    }

    [Test]
    public void ItDoesNotBank() {
        Allowance allowance = new(capacity: 10, perSecond: 2, Start);
        Assert.That(allowance.TryTake(10, Start, out allowance), Is.True);

        DateTimeOffset later = Start.AddHours(1);
        Assert.That(allowance.Available(later), Is.EqualTo(10), "an idle hour holds the capacity, not 7200 units");
        Assert.That(allowance.TryTake(10, later, out Allowance refilled), Is.True);
        Assert.That(refilled.TryTake(1, later, out _), Is.False);
    }

    [Test]
    public void ItNeverTakesMoreThanItsCapacity() {
        Allowance allowance = new(capacity: 10, perSecond: 2, Start);

        Assert.Multiple(() => {
            Assert.That(allowance.TryTake(11, Start, out _), Is.False);
            Assert.That(allowance.TryTake(11, Start.AddHours(1), out _), Is.False,
                "a take larger than the capacity waits forever, because the bucket can never hold it");
        });
    }

    [Test]
    public void ATakeOfNothingChangesNothing() {
        Allowance allowance = new(capacity: 4, perSecond: 1, Start);
        Assert.That(allowance.TryTake(3, Start, out allowance), Is.True);

        Assert.Multiple(() => {
            Assert.That(allowance.TryTake(0, Start, out Allowance after), Is.True);
            Assert.That(after.Available(Start), Is.EqualTo(1));
            Assert.That(after.TryTake(1, Start, out _), Is.True);
        });
    }

    [Test]
    public void AvailableIsWhatATakeWouldAccept() {
        Allowance allowance = new(capacity: 10, perSecond: 2, Start);
        Assert.That(allowance.TryTake(10, Start, out allowance), Is.True);
        DateTimeOffset half = Start.AddSeconds(0.5);

        Assert.Multiple(() => {
            Assert.That(allowance.Available(half), Is.EqualTo(1));
            Assert.That(allowance.TryTake(allowance.Available(half), half, out _), Is.True);
        });
    }

    [Test]
    public void AClockBehindTheLastRefillMintsNothing() {
        Allowance allowance = new(capacity: 10, perSecond: 10, Start);
        Assert.That(allowance.TryTake(10, Start, out allowance), Is.True);

        Assert.Multiple(() => {
            Assert.That(allowance.TryTake(1, Start.AddHours(-1), out Allowance behind), Is.False,
                "an instant before the last one refills nothing");
            Assert.That(behind.TryTake(10, Start.AddSeconds(1), out _), Is.True,
                "the last refill stays where it was, so the clock correction did not hand out a second burst");
        });
    }

    [Test]
    public void AnAllowanceThatCouldNeverTakeIsRefused() {
        Assert.Multiple(() => {
            Assert.That(() => new Allowance(capacity: 0, perSecond: 1, Start), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Allowance(capacity: 1, perSecond: 0, Start), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Allowance(capacity: 1, perSecond: -1, Start), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Allowance(capacity: 1, perSecond: 1, Start).TryTake(-1, Start, out _),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }
}
