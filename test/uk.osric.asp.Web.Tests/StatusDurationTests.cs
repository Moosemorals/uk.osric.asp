// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class StatusDurationTests {

    [Test]
    public void TheTokensAreTheOnesTheNavShows() {
        Assert.That(
            StatusDuration.All.Select(duration => duration.Token),
            Is.EqualTo((string[])["1h", "3h", "8h", "1d", "7d", "30d"]));
    }

    [Test]
    public void EachTokenSelectsItsOwnWindow() {
        Assert.That(StatusDuration.FromToken("1h").Window, Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(StatusDuration.FromToken("1d").Window, Is.EqualTo(TimeSpan.FromDays(1)));
        Assert.That(StatusDuration.FromToken("30d").Window, Is.EqualTo(TimeSpan.FromDays(30)));
    }

    [Test]
    public void AnUnknownTokenIsTheDefaultWindow() {
        Assert.That(StatusDuration.FromToken(null), Is.EqualTo(StatusDuration.Default));
        Assert.That(StatusDuration.FromToken("2w"), Is.EqualTo(StatusDuration.Default));
        Assert.That(StatusDuration.FromToken("1H"), Is.EqualTo(StatusDuration.Default));
    }

    [Test]
    public void ALongerWindowIsDrawnWider() {
        Assert.That(
            StatusDuration.All.Select(duration => duration.ChartWidth),
            Is.Ordered.Ascending);
    }

    [Test]
    public void ALongWindowSamplesMoreSparsely() {
        Assert.That(StatusDuration.FromToken("1h").Step, Is.EqualTo(TimeSpan.FromMinutes(1)));
        Assert.That(StatusDuration.FromToken("1d").Step, Is.EqualTo(TimeSpan.FromMinutes(3)));

        foreach (StatusDuration duration in StatusDuration.All) {
            Assert.That(
                duration.Window / duration.Step,
                Is.LessThanOrEqualTo(500),
                $"{duration.Token} asks for more samples than the chart can draw");
        }
    }
}
