// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageSearchOptionsTests {

    [TestCase("DefaultContextChars")]
    [TestCase("MaxContextChars")]
    public void ACountFromTheReleaseBeforeIsRefused(string retired) {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => PageSearchOptions.FromConfiguration(
            ShippedConfiguration.Read(($"Pages:Search:{retired}", "64"))))!;

        Assert.That(error.Message, Does.Contain($"Pages:Search:{retired} is no longer read").And.Contain("Bytes"));
    }

    [Test]
    public void TheShippedValuesAreTheDocumentedOnes() {
        Assert.Multiple(() => {
            Assert.That(TestOptions.Search.MaxDurationMs, Is.EqualTo(60));
            Assert.That(TestOptions.Search.DefaultMatches, Is.EqualTo(100));
            Assert.That(TestOptions.Search.MaxMatches, Is.EqualTo(1000));
            Assert.That(TestOptions.Search.DefaultContextBytes, Is.EqualTo(64));
            Assert.That(TestOptions.Search.MaxContextBytes, Is.EqualTo(1024));
            Assert.That(TestOptions.Search.Duration, Is.EqualTo(TimeSpan.FromMilliseconds(60)));
        });
    }

    [Test]
    public void ConfiguredValuesAreRead() {
        PageSearchOptions options = PageSearchOptions.FromConfiguration(ShippedConfiguration.Read(
            ("Pages:Search:MaxDurationMs", "5"),
            ("Pages:Search:DefaultMatches", "10"),
            ("Pages:Search:MaxMatches", "20"),
            ("Pages:Search:DefaultContextBytes", "128"),
            ("Pages:Search:MaxContextBytes", "256")));

        Assert.Multiple(() => {
            Assert.That(options.MaxDurationMs, Is.EqualTo(5));
            Assert.That(options.DefaultMatches, Is.EqualTo(10));
            Assert.That(options.MaxMatches, Is.EqualTo(20));
            Assert.That(options.DefaultContextBytes, Is.EqualTo(128));
            Assert.That(options.MaxContextBytes, Is.EqualTo(256));
        });
    }

    [Test]
    public void AMissingSectionStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageSearchOptions.FromConfiguration(ShippedConfiguration.WithoutSection("Pages:Search")))!;

        Assert.That(error.Message, Does.Contain("Pages:Search:MaxDurationMs is not set."));
    }

    [Test]
    public void AnAllowanceBelowOneStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageSearchOptions.FromConfiguration(ShippedConfiguration.Read(("Pages:Search:MaxDurationMs", "0"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Search:MaxDurationMs is 0"));
    }

    [Test]
    public void AnAllowancePastTheCeilingStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageSearchOptions.FromConfiguration(ShippedConfiguration.Read(("Pages:Search:MaxDurationMs", "60001"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Search:MaxDurationMs is 60001"));
    }

    [Test]
    public void ADefaultAboveTheMatchLimitStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageSearchOptions.FromConfiguration(ShippedConfiguration.Read(
                ("Pages:Search:DefaultMatches", "20"), ("Pages:Search:MaxMatches", "10"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Search:DefaultMatches is 20, which is larger than MaxMatches (10)"));
    }

    [Test]
    public void AContextBelowTheFloorStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageSearchOptions.FromConfiguration(
                ShippedConfiguration.Read(("Pages:Search:DefaultContextBytes", "8"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Search:DefaultContextBytes is 8"));
    }

    [Test]
    public void ADefaultContextAboveTheLimitStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageSearchOptions.FromConfiguration(ShippedConfiguration.Read(
                ("Pages:Search:DefaultContextBytes", "512"), ("Pages:Search:MaxContextBytes", "128"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Search:DefaultContextBytes is 512, which is larger than MaxContextBytes (128)"));
    }

    [Test]
    public void AContextLimitAboveItsCeilingStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageSearchOptions.FromConfiguration(
                ShippedConfiguration.Read(("Pages:Search:MaxContextBytes", "20000"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Search:MaxContextBytes is 20000"));
    }
}
