// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using uk.osric.asp.Web.Pages;

/// <summary>Proves a budget may be a fraction of a second, and is never configured away.</summary>
[TestFixture]
public sealed class PageBudgetTests {

    [Test]
    public void AFractionalBudgetSurvivesConfiguration() {
        RenderOptions options = RenderOptions.FromConfiguration(Configuration("0.25"));

        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
    }

    [TestCase("0")]
    [TestCase("-5")]
    public void ABudgetThatWouldDisableTheLimitIsClampedToTheFloor(string configured) {
        RenderOptions options = RenderOptions.FromConfiguration(Configuration(configured));

        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(PageBudget.MinimumTimeoutSeconds)));
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

    private static IConfiguration Configuration(string seconds) {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pages:Render:TimeoutSeconds"] = seconds })
            .Build();
    }
}
