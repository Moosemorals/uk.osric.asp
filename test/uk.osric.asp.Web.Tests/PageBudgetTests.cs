// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

/// <summary>Proves a budget may be a fraction of a second, and is never configured away.</summary>
[TestFixture]
public sealed class PageBudgetTests {

    [Test]
    public void AFractionalBudgetSurvivesConfiguration() {
        RenderOptions options = RenderOptions.FromConfiguration(
            ShippedConfiguration.Read(("Pages:Render:TimeoutSeconds", "0.25")));

        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
    }

    [TestCase("0")]
    [TestCase("-5")]
    public void ABudgetThatWouldDisableTheLimitStopsTheSite(string configured) {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => RenderOptions.FromConfiguration(
            ShippedConfiguration.Read(("Pages:Render:TimeoutSeconds", configured))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Pages:Render:TimeoutSeconds"));
            Assert.That(error.Message, Does.Contain("0.05"));
        });
    }

    [Test]
    public void ABurstIsWhatARateSpendsInAWholeRender() {
        RenderOptions options = RenderOptions.FromConfiguration(ShippedConfiguration.Read());

        Assert.Multiple(() => {
            Assert.That(options.BurstFor(2), Is.EqualTo(10), "two per second in a five-second deadline is today's ten");
            Assert.That(options.BurstFor(104_858), Is.EqualTo(524_290), "the shipped read rate keeps its whole burst");
            Assert.That(options.BurstFor(0.01), Is.EqualTo(1),
                "a rate too slow to fill one unit in a deadline still starts with one, so a render can always fetch once");
        });
    }

    [Test]
    public void ABurstIsNeverBelowOneWhateverTheDeadline() {
        // A fractional deadline is a supported setting, and the shipped rates are modest, so the
        // product of the two is routinely below one unit. A burst of zero would refuse every fetch
        // forever rather than bound one.
        RenderOptions tight = RenderOptions.FromConfiguration(
            ShippedConfiguration.Read(("Pages:Render:TimeoutSeconds", "0.25")));

        Assert.Multiple(() => {
            Assert.That(tight.BurstFor(2), Is.EqualTo(1), "a quarter second at two per second rounds up to one");
            Assert.That(tight.BurstFor(0.01), Is.EqualTo(1), "so does a rate far below one per deadline");
            Assert.That(tight.BurstFor(1_024), Is.EqualTo(256), "a rate that fills the deadline is still floored");
        });
    }

    [Test]
    public void AFractionalBudgetIsDescribedInSeconds() {
        Assert.That(PageBudget.DescribeSeconds(TimeSpan.FromMilliseconds(250)), Is.EqualTo("0.25"));
    }

    [Test]
    public void AWholeNumberOfSecondsIsDescribedWithoutADecimalPoint() {
        Assert.That(PageBudget.DescribeSeconds(TimeSpan.FromSeconds(10)), Is.EqualTo("10"));
    }

    [Test]
    [SetCulture("de-DE")]
    public void ABudgetIsDescribedTheSameInEveryLocale() {
        Assert.That(PageBudget.DescribeSeconds(TimeSpan.FromMilliseconds(250)), Is.EqualTo("0.25"),
            "a message an author reads must not format the deadline the way the server's locale does");
    }
}
