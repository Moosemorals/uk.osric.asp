// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

/// <summary>Proves a rate whose burst is too small to spend stops the site, in the one report.</summary>
[TestFixture]
public sealed class PageAllowanceRulesTests {

    [Test]
    public void ARateTooSlowToFillOneUnitStopsTheSite() {
        ConfigurationReader pages = new(ShippedConfiguration.Read(), "Pages");
        RenderOptions render = RenderOptions.FromConfiguration(ShippedConfiguration.Read());

        PageAllowanceRules.RequireOneWholeUnit(pages, "Fetch:FetchesPerSecond", 0.01, render);

        ConfigurationException error = Assert.Throws<ConfigurationException>(pages.Complete)!;
        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Pages:Fetch:FetchesPerSecond is 0.01"));
            Assert.That(error.Message, Does.Contain("Pages:Render:TimeoutSeconds (5)"));
            Assert.That(error.Message, Does.Contain("at least 0.2"),
                "the refusal names the smallest rate that works, so an operator is not left guessing");
        });
    }

    [Test]
    public void ARateThatFillsOneUnitIsAccepted() {
        ConfigurationReader pages = new(ShippedConfiguration.Read(), "Pages");
        RenderOptions render = RenderOptions.FromConfiguration(ShippedConfiguration.Read());

        PageAllowanceRules.RequireOneWholeUnit(pages, "Fetch:FetchesPerSecond", 0.2, render);

        Assert.That(pages.Complete, Throws.Nothing);
    }

    [Test]
    public void ADeadlineTooShortForTheShippedRateIsCaughtTheSameWay() {
        ConfigurationReader pages = new(ShippedConfiguration.Read(), "Pages");
        RenderOptions render = RenderOptions.FromConfiguration(
            ShippedConfiguration.Read(("Pages:Render:TimeoutSeconds", "0.25")));

        PageAllowanceRules.RequireOneWholeUnit(pages, "Jobs:FetchesPerSecond", 2, render);

        ConfigurationException error = Assert.Throws<ConfigurationException>(pages.Complete)!;
        Assert.That(error.Message, Does.Contain("Pages:Jobs:FetchesPerSecond is 2"));
    }
}
