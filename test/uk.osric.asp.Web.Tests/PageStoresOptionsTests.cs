// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Configuration;
using uk.osric.asp.Web.Pages;

[TestFixture]
public sealed class PageStoresOptionsTests {

    [TestCase("Pages:Stores:MaxKeyChars")]
    [TestCase("Pages:Stores:Records:MaxNameChars")]
    [TestCase("Pages:Stores:Records:MaxResultChars")]
    [TestCase("Pages:Stores:Secrets:MaxNameChars")]
    public void ACountFromTheReleaseBeforeIsRefused(string retired) {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => PageStoresOptions.FromConfiguration(
            ShippedConfiguration.Read((retired, "10"))))!;

        Assert.That(error.Message, Does.Contain($"{retired} is no longer read").And.Contain("Bytes"));
    }

    [Test]
    public void TheShippedValuesAreTheDocumentedOnes() {
        PageStoresOptions stores = TestOptions.Stores;

        Assert.Multiple(() => {
            Assert.That(stores.MaxKeyBytes, Is.EqualTo(200));
            Assert.That(stores.CommandTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(stores.BusyTimeoutSeconds, Is.EqualTo(5));
            Assert.That(stores.Page, Is.EqualTo(new SubjectLimits(65_536, 256, 262_144)));
            Assert.That(stores.Visitor.Anonymous, Is.EqualTo(new SubjectLimits(4_096, 8, 16_384)));
            Assert.That(stores.Visitor.Browser, Is.EqualTo(new SubjectLimits(4_096, 8, 16_384)));
            Assert.That(stores.Visitor.SignedIn, Is.EqualTo(new SubjectLimits(16_384, 32, 65_536)));
            Assert.That(stores.Visitor.AnonymousIdle, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(stores.Visitor.BrowserLifetimeDays, Is.EqualTo(365));
            Assert.That(stores.Visitor.MaxLiveSessions, Is.EqualTo(2_048));
            Assert.That(stores.Messages, Is.EqualTo(new PageMessageOptions(16_384, 8, 65_536)));
            Assert.That(stores.Objects, Is.EqualTo(new PageObjectOptions(8_388_608, 67_108_864)));
            Assert.That(stores.Secrets, Is.EqualTo(new PageSecretOptions(100, 8_192, 16, 32_768)));
        });
    }

    [Test]
    public void ConfiguredValuesAreRead() {
        PageStoresOptions stores = PageStoresOptions.FromConfiguration(ShippedConfiguration.Read(
            ("Pages:Stores:MaxKeyBytes", "32"),
            ("Pages:Stores:CommandTimeoutSeconds", "9"),
            ("Pages:Stores:BusyTimeoutSeconds", "7"),
            ("Pages:Stores:Page:MaxValueBytes", "2048"),
            ("Pages:Stores:Page:MaxKeys", "4"),
            ("Pages:Stores:Page:MaxTotalBytes", "8192"),
            ("Pages:Stores:Visitor:Anonymous:MaxValueBytes", "1024"),
            ("Pages:Stores:Visitor:Anonymous:MaxKeys", "2"),
            ("Pages:Stores:Visitor:Anonymous:MaxTotalBytes", "4096"),
            ("Pages:Stores:Visitor:Anonymous:IdleMinutes", "5"),
            ("Pages:Stores:Visitor:Browser:MaxValueBytes", "1024"),
            ("Pages:Stores:Visitor:Browser:MaxKeys", "3"),
            ("Pages:Stores:Visitor:Browser:MaxTotalBytes", "4096"),
            ("Pages:Stores:Visitor:Browser:LifetimeDays", "10"),
            ("Pages:Stores:Visitor:SignedIn:MaxValueBytes", "1024"),
            ("Pages:Stores:Visitor:SignedIn:MaxKeys", "5"),
            ("Pages:Stores:Visitor:SignedIn:MaxTotalBytes", "4096"),
            ("Pages:Stores:Visitor:MaxLiveSessions", "64"),
            ("Pages:Stores:Messages:MaxMessageBytes", "2048"),
            ("Pages:Stores:Messages:MaxMessagesPerSubject", "3"),
            ("Pages:Stores:Messages:MaxSubjectBytes", "8192"),
            ("Pages:Stores:Objects:MaxObjectBytes", "2048"),
            ("Pages:Stores:Objects:MaxSubjectBytes", "8192"),
            ("Pages:Stores:Secrets:MaxNameBytes", "40"),
            ("Pages:Stores:Secrets:MaxSecretBytes", "512"),
            ("Pages:Stores:Secrets:MaxSecrets", "4"),
            ("Pages:Stores:Secrets:MaxSubjectBytes", "4096")));

        Assert.Multiple(() => {
            Assert.That(stores.MaxKeyBytes, Is.EqualTo(32));
            Assert.That(stores.CommandTimeout, Is.EqualTo(TimeSpan.FromSeconds(9)));
            Assert.That(stores.BusyTimeoutSeconds, Is.EqualTo(7));
            Assert.That(stores.Page, Is.EqualTo(new SubjectLimits(2_048, 4, 8_192)));
            Assert.That(stores.Visitor.Anonymous, Is.EqualTo(new SubjectLimits(1_024, 2, 4_096)));
            Assert.That(stores.Visitor.Browser, Is.EqualTo(new SubjectLimits(1_024, 3, 4_096)));
            Assert.That(stores.Visitor.SignedIn, Is.EqualTo(new SubjectLimits(1_024, 5, 4_096)));
            Assert.That(stores.Visitor.AnonymousIdle, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(stores.Visitor.BrowserLifetimeDays, Is.EqualTo(10));
            Assert.That(stores.Visitor.MaxLiveSessions, Is.EqualTo(64));
            Assert.That(stores.Messages, Is.EqualTo(new PageMessageOptions(2_048, 3, 8_192)));
            Assert.That(stores.Objects, Is.EqualTo(new PageObjectOptions(2_048, 8_192)));
            Assert.That(stores.Secrets, Is.EqualTo(new PageSecretOptions(40, 512, 4, 4_096)));
        });
    }

    [Test]
    public void AMissingSectionStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageStoresOptions.FromConfiguration(ShippedConfiguration.WithoutSection("Pages:Stores")))!;

        Assert.That(error.Message, Does.Contain("Pages:Stores:MaxKeyBytes is not set."));
    }

    [Test]
    public void AnEmptyValueStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageStoresOptions.FromConfiguration(ShippedConfiguration.Read(("Pages:Stores:Page:MaxKeys", ""))))!;

        Assert.That(error.Message, Does.Contain("Pages:Stores:Page:MaxKeys is \"\""));
    }

    [Test]
    public void ATotalSmallerThanTheValueItHoldsStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageStoresOptions.FromConfiguration(ShippedConfiguration.Read(
                ("Pages:Stores:Page:MaxValueBytes", "4096"),
                ("Pages:Stores:Page:MaxTotalBytes", "2048"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Stores:Page:MaxTotalBytes is 2048, which is smaller than MaxValueBytes (4096)"));
    }

    [Test]
    public void AMessageTotalSmallerThanOneBodyStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageStoresOptions.FromConfiguration(ShippedConfiguration.Read(
                ("Pages:Stores:Messages:MaxMessageBytes", "4096"),
                ("Pages:Stores:Messages:MaxSubjectBytes", "2048"))))!;

        Assert.That(error.Message, Does.Contain(
            "Pages:Stores:Messages:MaxSubjectBytes is 2048, which is smaller than MaxMessageBytes (4096)"));
    }

    [Test]
    public void AnObjectTotalSmallerThanOneObjectStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageStoresOptions.FromConfiguration(ShippedConfiguration.Read(
                ("Pages:Stores:Objects:MaxObjectBytes", "4096"),
                ("Pages:Stores:Objects:MaxSubjectBytes", "2048"))))!;

        Assert.That(error.Message, Does.Contain(
            "Pages:Stores:Objects:MaxSubjectBytes is 2048, which is smaller than MaxObjectBytes (4096)"));
    }

    [Test]
    public void ASecretTotalSmallerThanOneSecretStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageStoresOptions.FromConfiguration(ShippedConfiguration.Read(
                ("Pages:Stores:Secrets:MaxSecretBytes", "2048"),
                ("Pages:Stores:Secrets:MaxSubjectBytes", "1024"))))!;

        Assert.That(error.Message, Does.Contain(
            "Pages:Stores:Secrets:MaxSubjectBytes is 1024, which is smaller than MaxSecretBytes (2048)"));
    }

    [Test]
    public void ATimeoutOfZeroStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageStoresOptions.FromConfiguration(ShippedConfiguration.Read(("Pages:Stores:CommandTimeoutSeconds", "0"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Stores:CommandTimeoutSeconds is 0"));
    }

    [Test]
    public void AKeyLimitBelowTheShortestUsefulKeyStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            PageStoresOptions.FromConfiguration(ShippedConfiguration.Read(("Pages:Stores:MaxKeyBytes", "4"))))!;

        Assert.That(error.Message, Does.Contain("Pages:Stores:MaxKeyBytes is 4"));
    }
}
