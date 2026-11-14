// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Metrics;

[TestFixture]
public sealed class MetricsOptionsTests {

    [Test]
    public void TheShippedValuesAreTheDocumentedOnes() {
        Assert.Multiple(() => {
            Assert.That(TestOptions.Metrics.PrometheusUrl, Is.EqualTo(new Uri("http://prometheus:9090")));
            Assert.That(TestOptions.Metrics.TimeZone.Id, Is.EqualTo("Europe/London"));
            Assert.That(TestOptions.Metrics.QueryTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(TestOptions.Metrics.CacheFor, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(TestOptions.Metrics.MaxCachedWindows, Is.EqualTo(16));
        });
    }

    [Test]
    public void ConfiguredValuesAreRead() {
        MetricsOptions options = MetricsOptions.FromConfiguration(ShippedConfiguration.Read(
            ("Metrics:PrometheusUrl", "https://metrics.example:9090/"),
            ("Metrics:TimeZone", "UTC"),
            ("Metrics:QueryTimeoutSeconds", "0.5"),
            ("Metrics:CacheSeconds", "0"),
            ("Metrics:MaxCachedWindows", "4")));

        Assert.Multiple(() => {
            Assert.That(options.PrometheusUrl, Is.EqualTo(new Uri("https://metrics.example:9090/")));
            Assert.That(options.TimeZone, Is.EqualTo(TimeZoneInfo.Utc));
            Assert.That(options.QueryTimeout, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(options.CacheFor, Is.EqualTo(TimeSpan.Zero));
            Assert.That(options.MaxCachedWindows, Is.EqualTo(4));
        });
    }

    [Test]
    public void AMissingSectionStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            MetricsOptions.FromConfiguration(ShippedConfiguration.WithoutSection("Metrics")))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Metrics:PrometheusUrl is not set."));
            Assert.That(error.Message, Does.Contain("Metrics:CacheSeconds is not set."));
        });
    }

    [Test]
    public void ARelativeUrlStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => MetricsOptions.FromConfiguration(
            ShippedConfiguration.Read(("Metrics:PrometheusUrl", "prometheus:9090"))))!;

        Assert.That(error.Message, Does.Contain("Metrics:PrometheusUrl is \"prometheus:9090\""));
    }

    [Test]
    public void AnUnknownTimeZoneStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => MetricsOptions.FromConfiguration(
            ShippedConfiguration.Read(("Metrics:TimeZone", "Mars/Olympus"))))!;

        Assert.That(error.Message, Does.Contain("Europe/London"));
    }

    [Test]
    public void AQueryTimeoutOutsideTheRangeStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => MetricsOptions.FromConfiguration(
            ShippedConfiguration.Read(("Metrics:QueryTimeoutSeconds", "0"))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Metrics:QueryTimeoutSeconds"));
            Assert.That(error.Message, Does.Contain("2 is recommended"));
        });
    }
}
