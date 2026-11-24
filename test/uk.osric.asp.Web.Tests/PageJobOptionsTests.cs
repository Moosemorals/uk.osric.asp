// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

/// <summary>The values <c>Pages:Jobs</c> supplies, and the ones it refuses.</summary>
[TestFixture]
public sealed class PageJobOptionsTests {

    [Test]
    public void TheRetiredReadRateStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => PageJobOptions.FromConfiguration(
            ShippedConfiguration.Read(("Pages:Jobs:SourceCharsPerSecond", "4096"))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Pages:Jobs:SourceCharsPerSecond is no longer read"));
            Assert.That(error.Message, Does.Contain("SourceBytesPerSecond"));
        });
    }

    [Test]
    public void TheShippedValuesAreTheOnesTheSiteRunsWith() {
        PageJobOptions options = PageJobOptions.FromConfiguration(ShippedConfiguration.Read());

        Assert.Multiple(() => {
            Assert.That(options.QueueCapacity, Is.EqualTo(8));
            Assert.That(options.MaxConcurrent, Is.EqualTo(2));
            Assert.That(options.SilenceSeconds, Is.EqualTo(120));
            Assert.That(options.MaxDurationSeconds, Is.EqualTo(32_400));
            Assert.That(options.FetchesPerSecond, Is.EqualTo(2));
            Assert.That(options.SourceBytesPerSecond, Is.EqualTo(104_858));
            Assert.That(options.MaxEvents, Is.EqualTo(512));
            Assert.That(options.MaxEventBytes, Is.EqualTo(256 * 1024));
            Assert.That(options.RetentionMinutes, Is.EqualTo(30));
            Assert.That(options.MaxWaitSeconds, Is.EqualTo(30));
            Assert.That(options.MaxMessageBytes, Is.EqualTo(16 * 1024));
            Assert.That(options.MaxPendingMessages, Is.EqualTo(32));
        });
    }

    [Test]
    public void AnOverrideIsRead() {
        PageJobOptions options = Options(
            ("Pages:Jobs:QueueCapacity", "3"),
            ("Pages:Jobs:MaxConcurrent", "1"),
            ("Pages:Jobs:SilenceSeconds", "5"),
            ("Pages:Jobs:MaxEvents", "16"),
            ("Pages:Jobs:FetchesPerSecond", "0.5"),
            ("Pages:Jobs:SourceBytesPerSecond", "2048"));

        Assert.Multiple(() => {
            Assert.That(options.QueueCapacity, Is.EqualTo(3));
            Assert.That(options.MaxConcurrent, Is.EqualTo(1));
            Assert.That(options.Silence, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(options.MaxEvents, Is.EqualTo(16));
            Assert.That(options.FetchesPerSecond, Is.EqualTo(0.5));
            Assert.That(options.SourceBytesPerSecond, Is.EqualTo(2048));
        });
    }

    [TestCase("QueueCapacity", "0")]
    [TestCase("MaxConcurrent", "0")]
    [TestCase("SilenceSeconds", "0")]
    [TestCase("MaxDurationSeconds", "-1")]
    [TestCase("MaxEvents", "0")]
    [TestCase("MaxEventBytes", "10")]
    [TestCase("RetentionMinutes", "0")]
    [TestCase("MaxWaitSeconds", "0")]
    [TestCase("MaxMessageBytes", "1")]
    [TestCase("MaxPendingMessages", "0")]
    [TestCase("FetchesPerSecond", "0")]
    [TestCase("SourceBytesPerSecond", "1")]
    public void AnUnusableValueStopsStartup(string key, string value) {
        ConfigurationException? exception = Assert.Throws<ConfigurationException>(
            () => Options(($"Pages:Jobs:{key}", value)));

        Assert.That(exception?.Message, Does.Contain($"Pages:Jobs:{key}"));
    }

    [Test]
    public void AMissingSectionStopsStartup() {
        ConfigurationException? exception = Assert.Throws<ConfigurationException>(
            () => PageJobOptions.FromConfiguration(ShippedConfiguration.WithoutSection("Pages:Jobs")));

        Assert.That(exception?.Message, Does.Contain("Pages:Jobs:QueueCapacity"));
    }

    [Test]
    public void AMissingValueStopsStartupAndNamesTheRange() {
        ConfigurationException? exception = Assert.Throws<ConfigurationException>(
            () => PageJobOptions.FromConfiguration(ShippedConfiguration.Without("Pages:Jobs:MaxEvents")));

        Assert.That(exception?.Message, Does.Contain("Pages:Jobs:MaxEvents").And.Contain("512"));
    }

    private static PageJobOptions Options(params (string Key, string? Value)[] overrides) =>
        PageJobOptions.FromConfiguration(ShippedConfiguration.Read(overrides));
}
