// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

/// <summary>How <c>Pages:Layout</c> and <c>Pages:Render:MaxLayoutDepth</c> are read.</summary>
[TestFixture]
public sealed class PageLayoutConfigurationTests {

    [TestCase("/_layout", "/_layout")]
    [TestCase("/Layout", "/layout")]
    [TestCase("_layout", "/_layout")]
    [TestCase("/_layout/", "/_layout")]
    [TestCase("/a/../_layout", "/_layout")]
    public void TheDefaultLayoutComesFromConfigurationAndNormalises(string configured, string expected) {
        PageLayoutConfiguration options = PageLayoutConfiguration.FromConfiguration(
            ShippedConfiguration.Read(("Pages:Layout", configured)));

        Assert.That(options.Default, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ABlankDefaultMeansNoLayout(string configured) {
        PageLayoutConfiguration options = PageLayoutConfiguration.FromConfiguration(
            ShippedConfiguration.Read(("Pages:Layout", configured)));

        Assert.That(options.Default, Is.Null);
    }

    [Test]
    public void AMissingLayoutStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageLayoutConfiguration.FromConfiguration(ShippedConfiguration.Without("Pages:Layout")))!;

        Assert.That(error.Message, Does.Contain("Pages:Layout is not set."));
    }

    [TestCase(1)]
    [TestCase(16)]
    public void TheLayoutDepthComesFromConfiguration(int configured) {
        RenderOptions options = RenderOptions.FromConfiguration(ShippedConfiguration.Read(
            ("Pages:Render:MaxLayoutDepth", configured.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        Assert.That(options.MaxLayoutDepth, Is.EqualTo(configured));
    }

    [TestCase(0)]
    [TestCase(-3)]
    [TestCase(400)]
    public void ALayoutDepthOutsideTheRangeStopsTheSite(int configured) {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => RenderOptions.FromConfiguration(
            ShippedConfiguration.Read(
                ("Pages:Render:MaxLayoutDepth", configured.ToString(System.Globalization.CultureInfo.InvariantCulture)))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Pages:Render:MaxLayoutDepth"));
            Assert.That(error.Message, Does.Contain("from 1 to 16"));
        });
    }
}
