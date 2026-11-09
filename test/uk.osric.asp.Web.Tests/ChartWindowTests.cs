// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class ChartWindowTests {

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-11-09T12:34:56Z", System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public void TheTokensAreTheOnesTheNavShows() {
        Assert.That(
            ChartWindow.Tokens.Select(window => window.Token),
            Is.EqualTo((string[])["1h", "3h", "8h", "1d", "7d", "30d"]));
    }

    [Test]
    public void EachTokenSelectsItsOwnLength() {
        Assert.That(ChartWindow.LengthForToken("1h"), Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(ChartWindow.LengthForToken("1d"), Is.EqualTo(TimeSpan.FromDays(1)));
        Assert.That(ChartWindow.LengthForToken("30d"), Is.EqualTo(TimeSpan.FromDays(30)));
    }

    [Test]
    public void AnUnknownTokenIsTheDefaultWindow() {
        Assert.That(ChartWindow.LengthForToken(null), Is.EqualTo(ChartWindow.DefaultLength));
        Assert.That(ChartWindow.LengthForToken("2w"), Is.EqualTo(ChartWindow.DefaultLength));
        Assert.That(ChartWindow.LengthForToken("1H"), Is.EqualTo(ChartWindow.DefaultLength));
        Assert.That(ChartWindow.TokenFor(null), Is.EqualTo("1h"));
        Assert.That(ChartWindow.TokenFor("2w"), Is.EqualTo("1h"));
    }

    [Test]
    public void ALongWindowSamplesMoreSparsely() {
        Assert.That(ChartWindow.StepFor(TimeSpan.FromHours(1)), Is.EqualTo(TimeSpan.FromMinutes(1)));
        Assert.That(ChartWindow.StepFor(TimeSpan.FromDays(1)), Is.EqualTo(TimeSpan.FromMinutes(3)));

        foreach (ChartWindow.WindowToken window in ChartWindow.Tokens) {
            Assert.That(
                window.Length / ChartWindow.StepFor(window.Length),
                Is.LessThanOrEqualTo(500),
                $"{window.Token} asks for more samples than the chart can draw");
        }
    }

    [Test]
    public void TheStartIsSnappedToTheStep() {
        ChartWindow window = ChartWindow.At(Now, TimeSpan.FromHours(1));

        Assert.That(window.Start, Is.EqualTo(DateTimeOffset.Parse("2026-11-09T12:34:00Z", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(window.End, Is.EqualTo(window.Start + window.Length));
        Assert.That(window.Step, Is.EqualTo(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public void ATokenWindowEndsAtTheStepThatHoldsNow() {
        ChartWindow window = ChartWindow.FromToken("7d", Now);

        Assert.That(window.Length, Is.EqualTo(TimeSpan.FromDays(7)));
        Assert.That(window.End, Is.LessThanOrEqualTo(Now));
        Assert.That(Now - window.End, Is.LessThan(window.Step), "the window ends within one step of now");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AWindowWithoutALengthIsRefused(int seconds) {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChartWindow.At(Now, TimeSpan.FromSeconds(seconds)));
    }
}
