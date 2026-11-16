// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Options;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Configuration;

[TestFixture]
public sealed class AssistantOptionsTests {

    private readonly AssistantOptionsValidator _validator = new();

    [Test]
    public void TheShippedConfigurationIsValid() {
        Assert.That(Validate(TestOptions.Assistant).Failed, Is.False);
    }

    [Test]
    public void EveryValueIsRequired() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => AssistantOptions.FromConfiguration(
            ShippedConfiguration.WithoutSection("Assistant")))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Assistant:MaxSteps is not set."));
            Assert.That(error.Message, Does.Contain("Assistant:MaxSearchMatches is not set."));
        });
    }

    [Test]
    public void AnOutOfRangeValueStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => AssistantOptions.FromConfiguration(
            ShippedConfiguration.Read(("Assistant:MaxSteps", "0"))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Assistant:MaxSteps is 0"));
            Assert.That(error.Message, Does.Contain("160 is recommended"));
        });
    }

    [Test]
    public void AListingDefaultLargerThanTheMaximumStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => AssistantOptions.FromConfiguration(
            ShippedConfiguration.Read(("Assistant:DefaultListLimit", "600"))))!;

        Assert.That(error.Message, Does.Contain("Assistant:DefaultListLimit is 600, which is larger than MaxListLimit"));
    }

    [Test]
    public void AMissingKeyStopsTheSiteWithoutNamingItInTheError() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => AssistantOptions.FromConfiguration(
            ShippedConfiguration.Read()))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Assistant:ApiKey is not set."));
            Assert.That(error.Message, Does.Not.Contain("ApiKey="));
        });
    }

    [Test]
    public void AProviderThatNeedsNoKeyIsAllowedToHaveNone() {
        AssistantOptions options = AssistantOptions.FromConfiguration(ShippedConfiguration.Read(
            ("Assistant:RequireApiKey", "false")));

        Assert.That(options.ApiKey, Is.Null);
    }

    [Test]
    public void LocalHttpProviderIsAllowedWithoutTheInsecureHttpSwitch() {
        AssistantOptions options = TestOptions.Assistant with {
            RequireApiKey = false,
            BaseUrl = "http://127.0.0.1:11434/v1",
        };

        AssertValid(options);
    }

    [Test]
    public void InsecureRemoteProviderIsRejected() {
        AssistantOptions options = TestOptions.Assistant with {
            ApiKey = "configured",
            BaseUrl = "http://provider.example",
        };

        Assert.That(Validate(options).FailureMessage, Does.Contain("HTTPS"));
    }

    [Test]
    public void ACredentialBearingBaseUrlIsRejected() {
        AssistantOptions options = TestOptions.Assistant with {
            ApiKey = "configured",
            BaseUrl = "https://user:secret@provider.example/v1",
        };

        Assert.That(Validate(options).FailureMessage, Does.Contain("must not contain credentials"));
    }

    [Test]
    public void ARelativeChatPathIsRejected() {
        AssistantOptions options = TestOptions.Assistant with {
            ApiKey = "configured",
            ChatCompletionsPath = "chat/completions",
        };

        Assert.That(Validate(options).FailureMessage, Does.Contain("ChatCompletionsPath"));
    }

    [Test]
    public void ABudgetAndOutputOverTheModelWindowAreRejected() {
        AssistantOptions options = TestOptions.Assistant with {
            ApiKey = "configured",
            MaxContextTokens = 1_000_000,
            MaxOutputTokens = 393_216,
            ModelContextTokens = 1_048_576,
        };

        Assert.Multiple(() => {
            Assert.That(Validate(options).Failed, Is.True);
            Assert.That(Validate(options).FailureMessage, Does.Contain("ModelContextTokens"));
        });
    }

    [Test]
    public void AMemoryLongerThanOneRunsMemoryBlockIsRejected() {
        AssistantOptions options = TestOptions.Assistant with {
            ApiKey = "configured",
            MaxMemoryChars = 5_000,
            MaxMemoryCharsPerRun = 4_000,
        };

        Assert.That(Validate(options).FailureMessage, Does.Contain("MaxMemoryCharsPerRun"));
    }

    [Test]
    public void TheBalanceSamplerShipsAtAMinute() {
        Assert.That(TestOptions.Assistant.BalanceSampleSeconds, Is.EqualTo(60));
    }

    [Test]
    public void TheBalanceSampleCadenceCanBeOverridden() {
        AssistantOptions options = AssistantOptions.FromConfiguration(
            ShippedConfiguration.Read(("Assistant:ApiKey", "test-key"), ("Assistant:BalanceSampleSeconds", "120")));

        Assert.That(options.BalanceSampleSeconds, Is.EqualTo(120));
    }

    [Test]
    public void ABalanceSampleCadenceOutsideTheRangeStopsTheSite() {
        ConfigurationException error = Assert.Throws<ConfigurationException>(() => AssistantOptions.FromConfiguration(
            ShippedConfiguration.Read(("Assistant:ApiKey", "test-key"), ("Assistant:BalanceSampleSeconds", "10"))))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain("Assistant:BalanceSampleSeconds is 10"));
            Assert.That(error.Message, Does.Contain("30 to 3600"));
            Assert.That(error.Message, Does.Contain("60 is recommended"));
        });
    }

    private void AssertValid(AssistantOptions options) => Assert.That(Validate(options).Failed, Is.False);

    private ValidateOptionsResult Validate(AssistantOptions options) => _validator.Validate(name: null, options);
}
