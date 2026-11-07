// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Charting;

[TestFixture]
public sealed class NiceTicksTests {

    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    private static string Labels(IReadOnlyList<ChartTick> ticks) => string.Join(' ', ticks.Select(tick => tick.Label));

    [Test]
    public void ValueTicksUseOneTwoFiveSteps() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(0, 10);

        Assert.That(Labels(ticks), Is.EqualTo("0 2 4 6 8 10"));
    }

    [Test]
    public void ValueTicksKeepFractionsForSmallRanges() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(0, 0.5);

        Assert.That(Labels(ticks), Is.EqualTo("0.0 0.1 0.2 0.3 0.4 0.5"));
    }

    [Test]
    public void EveryTickOnAnAxisCarriesTheSamePrecision() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(0.00025, 0.000475);

        Assert.That(
            Labels(ticks),
            Is.EqualTo("0.00025 0.00030 0.00035 0.00040 0.00045 0.00050"));
    }

    [Test]
    public void AFlatValueRangeIsWidened() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(3, 3);

        Assert.That(ticks.Count, Is.GreaterThan(1));
        Assert.That(ticks[^1].Position - ticks[0].Position, Is.GreaterThan(0));
    }

    [Test]
    public void ANonFiniteValueRangeFallsBackToZeroToOne() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(double.NaN, double.NaN);

        Assert.That(ticks[0].Position, Is.EqualTo(0));
        Assert.That(ticks[^1].Position, Is.EqualTo(1));
    }

    [Test]
    public void TimeTicksAreSecondsAfterTheStart() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-01-15T13:00:00Z"), At("2026-01-15T13:10:00Z"), London);

        Assert.That(string.Join(' ', ticks.Select(tick => tick.Position)), Is.EqualTo("0 300 600"));
        Assert.That(Labels(ticks), Is.EqualTo("13:00 13:05 13:10"));
    }

    [Test]
    public void TimeLabelsUseTheZoneInSummer() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-07-01T12:00:00Z"), At("2026-07-01T12:10:00Z"), London);

        Assert.That(Labels(ticks), Is.EqualTo("13:00 13:05 13:10"));
    }

    [Test]
    public void TheRepeatedOctoberHourYieldsTwoTicksReadingTheSameClockTime() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-10-25T00:00:00Z"), At("2026-10-25T02:00:00Z"), London);

        Assert.That(ticks.Count(tick => tick.Label == "01:00"), Is.EqualTo(2));
    }

    [Test]
    public void SpansOverADayCarryTheDate() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-06-01T00:00:00Z"), At("2026-06-11T00:00:00Z"), London);

        Assert.That(ticks, Is.Not.Empty);
        Assert.That(ticks.All(tick => tick.Label.Contains(" Jun ", StringComparison.Ordinal)), Is.True);
        Assert.That(ticks.All(tick => tick.Label.EndsWith("00:00", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SpansAcrossAYearCarryTheYear() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-12-30T00:00:00Z"), At("2027-01-03T00:00:00Z"), London);

        Assert.That(ticks.Any(tick => tick.Label.Contains("2026", StringComparison.Ordinal)), Is.True);
        Assert.That(ticks.Any(tick => tick.Label.Contains("2027", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void AZeroWidthSpanYieldsOneTick() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-01-15T13:00:00Z"), At("2026-01-15T13:00:00Z"), London);

        Assert.That(ticks, Has.Count.EqualTo(1));
        Assert.That(ticks[0].Position, Is.EqualTo(0));
    }

    [Test]
    public void ANullZoneIsRefused() {
        Assert.That(() => NiceTicks.ForTime(At("2026-01-15T13:00:00Z"), At("2026-01-15T13:10:00Z"), null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("zone"));
    }

    [Test]
    public void AZeroWidthSpanIsLabelledWithoutADate() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-01-15T13:00:00Z"), At("2026-01-15T13:00:00Z"), London);

        Assert.That(Labels(ticks), Is.EqualTo("13:00"));
    }

    [Test]
    public void AZeroWidthSpanStartingMidSecondStillYieldsOneTick() {
        IReadOnlyList<ChartTick> ticks =
            NiceTicks.ForTime(At("2026-01-15T13:00:00.5Z"), At("2026-01-15T13:00:00.5Z"), London);

        Assert.That(ticks, Has.Count.EqualTo(1));
    }

    [Test]
    public void ASpanOfExactlyADayCarriesTheDate() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-06-01T00:00:00Z"), At("2026-06-02T00:00:00Z"), London);

        Assert.That(ticks.All(tick => tick.Label.Contains(" Jun ", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void FourDaysOfTicksFallOnLocalMidnightAndKeepBothEdges() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-06-01T23:00:00Z"), At("2026-06-06T23:00:00Z"), London);

        Assert.That(ticks, Has.Count.EqualTo(6), "a tick on each edge, not six inside them");
        Assert.That(ticks[0].Label, Is.EqualTo("Tue 2 Jun 00:00"));
        Assert.That(ticks[^1].Label, Is.EqualTo("Sun 7 Jun 00:00"));
    }

    [Test]
    public void AThirtySecondSpanUsesFiveSecondTicks() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForTime(At("2026-01-15T13:00:00Z"), At("2026-01-15T13:00:30Z"), London);

        Assert.That(string.Join(' ', ticks.Select(tick => tick.Position)), Is.EqualTo("0 5 10 15 20 25 30"));
    }

    [Test]
    public void OneNonFiniteBoundStillFallsBackToZeroToOne() {
        IReadOnlyList<ChartTick> ticks = NiceTicks.ForValue(double.NaN, 10);

        Assert.That(ticks[0].Position, Is.EqualTo(0));
        Assert.That(ticks[^1].Position, Is.EqualTo(1));
    }

    [Test]
    public void ReversedBoundsAreSwapped() {
        Assert.That(Labels(NiceTicks.ForValue(10, 0)), Is.EqualTo("0 2 4 6 8 10"));
    }

    [Test]
    public void AFlatNonZeroRangeIsWidenedByAFractionOfItsValue() {
        Assert.That(Labels(NiceTicks.ForValue(3, 3)), Is.EqualTo("2.6 2.8 3.0 3.2 3.4"));
    }

    [Test]
    public void AFlatRangeAtZeroIsWidenedByOne() {
        Assert.That(Labels(NiceTicks.ForValue(0, 0)), Is.EqualTo("-1.0 -0.5 0.0 0.5 1.0"));
    }

    [Test]
    public void AStepPastFiveRoundsUpToTen() {
        Assert.That(Labels(NiceTicks.ForValue(0, 35)), Is.EqualTo("0 10 20 30 40"));
    }

    [Test]
    public void AStepOfExactlyFiveStaysFive() {
        Assert.That(Labels(NiceTicks.ForValue(0, 25)), Is.EqualTo("0 5 10 15 20 25"));
    }
}
