// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web;
using uk.osric.asp.Web.Configuration;

[TestFixture]
public sealed class SiteOptionsTests {

    [Test]
    public void TheShippedOriginIsTheContainerLoopback() {
        Assert.That(SiteOptions.FromConfiguration(ShippedConfiguration.Read()).Origin,
            Is.EqualTo(new Uri("http://127.0.0.1:8080")));
    }

    [Test]
    public void AConfiguredOriginIsRead() {
        SiteOptions options = SiteOptions.FromConfiguration(ShippedConfiguration.Read(
            ("Site:Origin", "https://moosemorals.com")));

        Assert.That(options.Origin, Is.EqualTo(new Uri("https://moosemorals.com")));
    }

    [Test]
    public void AMissingOriginStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            SiteOptions.FromConfiguration(ShippedConfiguration.Without("Site:Origin")))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Site:Origin is not set."));
            Assert.That(error.Message, Does.Contain(SiteOptions.Recommended));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ABlankOriginStopsTheSite(string origin) {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            SiteOptions.FromConfiguration(ShippedConfiguration.Read(("Site:Origin", origin))))!;

        Assert.That(error.Message, Does.Contain("Site:Origin is blank."));
    }

    [TestCase("moosemorals.com")]
    [TestCase("/moosemorals.com")]
    [TestCase("http://127.0.0.1:8080/site")]
    [TestCase("http://127.0.0.1:8080/?a=1")]
    [TestCase("http://127.0.0.1:8080/#top")]
    [TestCase("http://user:pass@127.0.0.1:8080")]
    [TestCase("ftp://127.0.0.1:8080")]
    [TestCase("http://moosemorals.com")]
    public void AnOriginThatIsNotAnOriginStopsTheSite(string origin) {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            SiteOptions.FromConfiguration(ShippedConfiguration.Read(("Site:Origin", origin))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Site:Origin"));
            Assert.That(error.Message, Does.Contain("plaintext http is accepted only for a loopback host"));
        });
    }

    [Test]
    public void APathIsJoinedOntoTheOrigin() {
        SiteOptions options = new() { Origin = new Uri(SiteOptions.Recommended) };

        Assert.Multiple(() => {
            Assert.That(options.TryResolve("/hello", out string hello, out _), Is.True);
            Assert.That(hello, Is.EqualTo("http://127.0.0.1:8080/hello"));
            Assert.That(options.TryResolve("/pages?edit=/hello", out string query, out _), Is.True);
            Assert.That(query, Is.EqualTo("http://127.0.0.1:8080/pages?edit=/hello"));
            Assert.That(options.TryResolve("/", out string root, out _), Is.True);
            Assert.That(root, Is.EqualTo("http://127.0.0.1:8080/"));
        });
    }

    [TestCase("")]
    [TestCase("hello")]
    [TestCase("https://example.com/x")]
    [TestCase("//example.com/x")]
    [TestCase("/hello#top")]
    public void APathThatNamesAnotherPlaceIsRefused(string path) {
        SiteOptions options = new() { Origin = new Uri(SiteOptions.Recommended) };

        Assert.That(options.TryResolve(path, out string url, out string? problem), Is.False);
        Assert.Multiple(() => {
            Assert.That(url, Is.Empty);
            Assert.That(problem, Is.Not.Null.And.Not.Empty);
        });
    }
}
