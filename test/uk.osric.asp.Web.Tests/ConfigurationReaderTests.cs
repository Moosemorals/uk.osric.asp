// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using uk.osric.asp.Web.Configuration;

[TestFixture]
public sealed class ConfigurationReaderTests {

    [Test]
    public void AMissingNumberNamesTheKeyTheRangeAndTheRecommendation() {
        ConfigurationReader reader = Reader();

        _ = reader.Number("Slots", recommended: 2, minimum: 1, maximum: 64);

        ConfigurationException error = Assert.Throws<ConfigurationException>(reader.Complete)!;
        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Example:Slots is not set."));
            Assert.That(error.Message, Does.Contain("a whole number from 1 to 64"));
            Assert.That(error.Message, Does.Contain("2 is recommended"));
        });
    }

    [Test]
    public void ANumberBelowTheMinimumNamesTheValueItFound() {
        ConfigurationReader reader = Reader(("Example:Slots", "0"));

        _ = reader.Number("Slots", recommended: 2, minimum: 1, maximum: 64);

        ConfigurationException error = Assert.Throws<ConfigurationException>(reader.Complete)!;
        Assert.That(error.Message, Does.Contain("Example:Slots is 0, which is outside the accepted range of 1 to 64."));
    }

    [Test]
    public void AValueThatIsNotANumberIsReportedRatherThanAssumed() {
        ConfigurationReader reader = Reader(("Example:Slots", "two"));

        _ = reader.Number("Slots", recommended: 2, minimum: 1, maximum: 64);

        Assert.That(Assert.Throws<ConfigurationException>(reader.Complete)!.Message,
            Does.Contain("Example:Slots is \"two\", which is not a whole number."));
    }

    [Test]
    public void AConfiguredNumberIsRead() {
        ConfigurationReader reader = Reader(("Example:Slots", "7"), ("Example:Seconds", "0.25"));

        Assert.Multiple(() => {
            Assert.That(reader.Number("Slots", recommended: 2, minimum: 1, maximum: 64), Is.EqualTo(7));
            Assert.That(reader.Decimal("Seconds", recommended: 5, minimum: 0.05, maximum: 60), Is.EqualTo(0.25));
            Assert.That(reader.Complete, Throws.Nothing);
        });
    }

    [Test]
    public void ABlankStringIsReportedUnlessBlankIsMeaningful() {
        ConfigurationReader reader = Reader(("Example:Key", " "));

        Assert.Multiple(() => {
            Assert.That(reader.Text("Key", "a page key such as /pages"), Is.Empty);
            Assert.That(reader.Complete, Throws.TypeOf<ConfigurationException>());
        });
    }

    [Test]
    public void ABlankStringIsAcceptedWhereBlankHasAMeaning() {
        ConfigurationReader reader = Reader(("Example:Layout", ""));

        Assert.That(reader.Text("Layout", "a page key such as /_layout", allowEmpty: true), Is.Empty);
        Assert.That(reader.Complete, Throws.Nothing);
    }

    [Test]
    public void ASwitchMustBeTrueOrFalse() {
        ConfigurationReader reader = Reader(("Example:Enabled", "sometimes"));

        _ = reader.Switch("Enabled", recommended: false);

        Assert.That(Assert.Throws<ConfigurationException>(reader.Complete)!.Message,
            Does.Contain("Example:Enabled is \"sometimes\", which is not true or false."));
    }

    [Test]
    public void AListBelowItsMinimumIsReportedWithTheCountItFound() {
        ConfigurationReader reader = Reader(("Example:Paths", ""));

        _ = reader.List("Paths", "page keys", minimum: 1);

        Assert.That(Assert.Throws<ConfigurationException>(reader.Complete)!.Message,
            Does.Contain("Example:Paths has 0 entries"));
    }

    [Test]
    public void EveryProblemInTheSectionIsReportedAtOnce() {
        ConfigurationReader reader = Reader(("Example:Slots", "0"));

        _ = reader.Number("Slots", recommended: 2, minimum: 1, maximum: 64);
        _ = reader.Text("Name", "a name");
        _ = reader.Switch("Enabled", recommended: true);

        ConfigurationException error = Assert.Throws<ConfigurationException>(reader.Complete)!;
        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Example:Slots"));
            Assert.That(error.Message, Does.Contain("Example:Name"));
            Assert.That(error.Message, Does.Contain("Example:Enabled"));
        });
    }

    [Test]
    public void ANestedReaderNamesTheWholeKey() {
        ConfigurationReader reader = Reader();

        _ = reader.For("Login").Number("PermitLimit", recommended: 10, minimum: 1, maximum: 1_000);

        Assert.That(Assert.Throws<ConfigurationException>(reader.Complete)!.Message,
            Does.Contain("Example:Login:PermitLimit is not set."));
    }

    [Test]
    public void AReaderOpenedAtAnotherSectionNamesThatSection() {
        ConfigurationReader reader = Reader(("Storage:MaxKeyChars", "0"));

        _ = reader.At("Storage").Number("MaxKeyChars", recommended: 200, minimum: 16, maximum: 4_096);

        Assert.That(Assert.Throws<ConfigurationException>(reader.Complete)!.Message,
            Does.Contain("Storage:MaxKeyChars is 0"));
    }

    [Test]
    public void TheReportSaysWhereToSetTheValue() {
        ConfigurationReader reader = Reader();

        _ = reader.Text("Name", "a name");

        Assert.That(Assert.Throws<ConfigurationException>(reader.Complete)!.Message,
            Does.Contain("Set these values in appsettings.json"));
    }

    [Test]
    public void ACompleteSectionReportsNothing() {
        ConfigurationReader reader = Reader(("Example:Name", "value"), ("Example:Enabled", "true"));

        Assert.Multiple(() => {
            Assert.That(reader.Text("Name", "a name"), Is.EqualTo("value"));
            Assert.That(reader.Switch("Enabled", recommended: false), Is.True);
            Assert.That(reader.Complete, Throws.Nothing);
        });
    }

    private static ConfigurationReader Reader(params (string Key, string Value)[] values) {
        Dictionary<string, string?> settings = [];
        foreach ((string key, string value) in values) {
            settings[key] = value;
        }

        return new ConfigurationReader(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), "Example");
    }
}
