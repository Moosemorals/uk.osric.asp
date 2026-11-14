// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Configuration;

[TestFixture]
public sealed class AuthenticationOptionsTests {

    [Test]
    public void TheShippedValuesAreTheDocumentedOnes() {
        Assert.Multiple(() => {
            Assert.That(TestOptions.Authentication.TicketLifetime, Is.EqualTo(TimeSpan.FromDays(30)));
            Assert.That(TestOptions.Authentication.Login.PermitLimit, Is.EqualTo(10));
            Assert.That(TestOptions.Authentication.Login.Window, Is.EqualTo(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public void ConfiguredValuesAreRead() {
        AuthenticationOptions options = AuthenticationOptions.FromConfiguration(ShippedConfiguration.Read(
            ("Authentication:TicketLifetimeDays", "7"),
            ("Authentication:Login:PermitLimit", "3"),
            ("Authentication:Login:WindowSeconds", "30")));

        Assert.Multiple(() => {
            Assert.That(options.TicketLifetime, Is.EqualTo(TimeSpan.FromDays(7)));
            Assert.That(options.Login.PermitLimit, Is.EqualTo(3));
            Assert.That(options.Login.Window, Is.EqualTo(TimeSpan.FromSeconds(30)));
        });
    }

    [Test]
    public void AMissingSectionStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() =>
            AuthenticationOptions.FromConfiguration(ShippedConfiguration.WithoutSection("Authentication")))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Authentication:TicketLifetimeDays is not set."));
            Assert.That(error.Message, Does.Contain("Authentication:Login:PermitLimit is not set."));
        });
    }

    [Test]
    public void AThrottleOfZeroStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => AuthenticationOptions.FromConfiguration(
            ShippedConfiguration.Read(("Authentication:Login:PermitLimit", "0"))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Authentication:Login:PermitLimit is 0"));
            Assert.That(error.Message, Does.Contain("10 is recommended"));
        });
    }

    [TestCase("0")]
    [TestCase("366")]
    public void ATicketLifetimeOutsideTheRangeStopsTheSite(string days) {
        Assert.That(
            () => AuthenticationOptions.FromConfiguration(
                ShippedConfiguration.Read(("Authentication:TicketLifetimeDays", days))),
            Throws.TypeOf<ConfigurationException>());
    }
}
