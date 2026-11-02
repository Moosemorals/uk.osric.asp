// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class NiceTicksTests {

    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    private static string Labels(IReadOnlyList<ChartTick> ticks) => string.Join(' ', ticks.Select(tick => tick.Label));

    [Test]
    public void ValueTicksUseOneTwoFiveSteps() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Value(0, 10);

        Assert.That(Labels(ticks), Is.EqualTo("0 2 4 6 8 10"));
    }

    [Test]
    public void ValueTicksKeepFractionsForSmallRanges() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Value(0, 0.5);

        Assert.That(Labels(ticks), Is.EqualTo("0 0.1 0.2 0.3 0.4 0.5"));
    }

    [Test]
    public void AFlatValueRangeIsWidened() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Value(3, 3);

        Assert.That(ticks.Count, Is.GreaterThan(1));
        Assert.That(ticks[^1].Position - ticks[0].Position, Is.GreaterThan(0));
    }

    [Test]
    public void ANonFiniteValueRangeFallsBackToZeroToOne() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Value(double.NaN, double.NaN);

        Assert.That(ticks[0].Position, Is.EqualTo(0));
        Assert.That(ticks[^1].Position, Is.EqualTo(1));
    }

    [Test]
    public void TimeTicksAreSecondsAfterTheStart() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Time(At("2026-01-15T13:00:00Z"), At("2026-01-15T13:10:00Z"), London);

        Assert.That(string.Join(' ', ticks.Select(tick => tick.Position)), Is.EqualTo("0 300 600"));
        Assert.That(Labels(ticks), Is.EqualTo("13:00 13:05 13:10"));
    }

    [Test]
    public void TimeLabelsUseTheZoneInSummer() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Time(At("2026-07-01T12:00:00Z"), At("2026-07-01T12:10:00Z"), London);

        Assert.That(Labels(ticks), Is.EqualTo("13:00 13:05 13:10"));
    }

    [Test]
    public void TheRepeatedOctoberHourYieldsTwoTicksReadingTheSameClockTime() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Time(At("2026-10-25T00:00:00Z"), At("2026-10-25T02:00:00Z"), London);

        Assert.That(ticks.Count(tick => tick.Label == "01:00"), Is.EqualTo(2));
    }

    [Test]
    public void SpansOverADayCarryTheDate() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Time(At("2026-06-01T00:00:00Z"), At("2026-06-11T00:00:00Z"), London);

        Assert.That(ticks, Is.Not.Empty);
        Assert.That(ticks.All(tick => tick.Label.Contains(" Jun ", StringComparison.Ordinal)), Is.True);
        Assert.That(ticks.All(tick => tick.Label.EndsWith("00:00", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SpansAcrossAYearCarryTheYear() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Time(At("2026-12-30T00:00:00Z"), At("2027-01-03T00:00:00Z"), London);

        Assert.That(ticks.Any(tick => tick.Label.Contains("2026", StringComparison.Ordinal)), Is.True);
        Assert.That(ticks.Any(tick => tick.Label.Contains("2027", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void AZeroWidthSpanYieldsOneTick() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.Time(At("2026-01-15T13:00:00Z"), At("2026-01-15T13:00:00Z"), London);

        Assert.That(ticks, Has.Count.EqualTo(1));
        Assert.That(ticks[0].Position, Is.EqualTo(0));
    }

    [Test]
    public void ANullZoneIsRefused() {
        Assert.Throws<ArgumentNullException>(
            () => NiceTicks.Time(At("2026-01-15T13:00:00Z"), At("2026-01-15T13:10:00Z"), null!));
    }
}
