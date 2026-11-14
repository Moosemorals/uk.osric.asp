// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageReadOptionsTests {

    [Test]
    public void TheShippedValuesAreTheDocumentedOnes() {
        Assert.Multiple(() => {
            Assert.That(TestOptions.Read.MaxRowsPerList, Is.EqualTo(500));
            Assert.That(TestOptions.Read.MaxSourceCharsPerRender, Is.EqualTo(512 * 1024));
        });
    }

    [Test]
    public void ConfiguredValuesAreRead() {
        PageReadOptions options = PageReadOptions.FromConfiguration(ShippedConfiguration.Read(
            ("Pages:Read:MaxRowsPerList", "50"),
            ("Pages:Read:MaxSourceCharsPerRender", "4096")));

        Assert.Multiple(() => {
            Assert.That(options.MaxRowsPerList, Is.EqualTo(50));
            Assert.That(options.MaxSourceCharsPerRender, Is.EqualTo(4096));
        });
    }

    [Test]
    public void AMissingSectionStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageReadOptions.FromConfiguration(ShippedConfiguration.WithoutSection("Pages:Read")))!;

        Assert.That(error.Message, Does.Contain("Pages:Read:MaxRowsPerList is not set."));
    }

    [Test]
    public void ARowLimitBelowOneStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageReadOptions.FromConfiguration(ShippedConfiguration.Read(("Pages:Read:MaxRowsPerList", "0"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Read:MaxRowsPerList is 0, which is outside the accepted range of 1 to 10000."));
    }

    [Test]
    public void ARowLimitAboveTheCeilingStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageReadOptions.FromConfiguration(ShippedConfiguration.Read(("Pages:Read:MaxRowsPerList", "100000"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Read:MaxRowsPerList is 100000"));
    }

    [Test]
    public void ASourceBudgetBelowTheFloorStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageReadOptions.FromConfiguration(ShippedConfiguration.Read(("Pages:Read:MaxSourceCharsPerRender", "1"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Read:MaxSourceCharsPerRender is 1"));
    }
}
