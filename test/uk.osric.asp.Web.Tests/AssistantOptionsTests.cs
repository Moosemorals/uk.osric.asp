// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Options;
using uk.osric.asp.Web.Assistant;

[TestFixture]
public sealed class AssistantOptionsTests {

    private readonly AssistantOptionsValidator _validator = new();

    [Test]
    public void DisabledAssistantDoesNotRequireProviderConfiguration() {
        AssertValid(new AssistantOptions());
    }

    [Test]
    public void EnabledAssistantRequiresAnApiKeyByDefaultWithoutNamingTheKeyInTheError() {
        AssistantOptions options = new() { Enabled = true };

        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("Assistant__ApiKey"));
        Assert.That(result.FailureMessage, Does.Not.Contain("ApiKey="));
    }

    [Test]
    public void LocalHttpProviderIsAllowedWithoutTheInsecureHttpSwitch() {
        AssistantOptions options = new() {
            Enabled = true,
            RequireApiKey = false,
            BaseUrl = "http://127.0.0.1:11434/v1",
        };

        AssertValid(options);
    }

    [Test]
    public void InsecureRemoteProviderIsRejected() {
        AssistantOptions options = new() {
            Enabled = true,
            RequireApiKey = false,
            BaseUrl = "http://provider.example",
        };

        Assert.That(Validate(options).FailureMessage, Does.Contain("HTTPS"));
    }

    [Test]
    public void EnabledAssistantRejectsNonPositiveRunLimits() {
        AssistantOptions options = new() {
            Enabled = true,
            ApiKey = "configured",
            MaxSteps = 0,
        };

        Assert.That(Validate(options).Failed, Is.True);
    }

    [Test]
    public void TheShippedMemoryLimitsAreValid() {
        AssertValid(Configured(maximumMemoriesPerOwner: 200, maximumMemoryChars: 1_000,
            maximumMemoriesPerRun: 12, maximumMemoryCharsPerRun: 4_000,
            maximumMemorySearchResults: 50, maximumMemoryQueryTerms: 12));
    }

    [TestCase(0, 1_000, 12, 4_000, 50, 12)]
    [TestCase(200, 0, 12, 4_000, 50, 12)]
    [TestCase(200, 1_000, 0, 4_000, 50, 12)]
    [TestCase(200, 1_000, 12, 0, 50, 12)]
    [TestCase(200, 1_000, 12, 4_000, 0, 12)]
    public void EveryMemoryLimitMustBePositive(
        int maximumMemoriesPerOwner,
        int maximumMemoryChars,
        int maximumMemoriesPerRun,
        int maximumMemoryCharsPerRun,
        int maximumMemorySearchResults,
        int maximumMemoryQueryTerms) {
        Assert.That(Validate(Configured(maximumMemoriesPerOwner, maximumMemoryChars, maximumMemoriesPerRun,
            maximumMemoryCharsPerRun, maximumMemorySearchResults, maximumMemoryQueryTerms)).Failed, Is.True);
    }

    [Test]
    public void AMemoryLongerThanTheWholeRunBlockIsRefused() {
        AssistantOptions options = Configured(maximumMemoriesPerOwner: 200, maximumMemoryChars: 5_000,
            maximumMemoriesPerRun: 12, maximumMemoryCharsPerRun: 4_000,
            maximumMemorySearchResults: 50, maximumMemoryQueryTerms: 12);

        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("MaxMemoryCharsPerRun"));
    }

    [Test]
    public void AQueryOfASingleLetterIsRefused() {
        AssistantOptions options = Configured(maximumMemoriesPerOwner: 200, maximumMemoryChars: 1_000,
            maximumMemoriesPerRun: 12, maximumMemoryCharsPerRun: 4_000,
            maximumMemorySearchResults: 50, maximumMemoryQueryTerms: 1);

        Assert.That(Validate(options).FailureMessage, Does.Contain("MaxMemoryQueryTerms"));
    }

    private static AssistantOptions Configured(
        int maximumMemoriesPerOwner,
        int maximumMemoryChars,
        int maximumMemoriesPerRun,
        int maximumMemoryCharsPerRun,
        int maximumMemorySearchResults,
        int maximumMemoryQueryTerms) => new() {
            Enabled = true,
            ApiKey = "configured",
            MaxMemoriesPerOwner = maximumMemoriesPerOwner,
            MaxMemoryChars = maximumMemoryChars,
            MaxMemoriesPerRun = maximumMemoriesPerRun,
            MaxMemoryCharsPerRun = maximumMemoryCharsPerRun,
            MaxMemorySearchResults = maximumMemorySearchResults,
            MaxMemoryQueryTerms = maximumMemoryQueryTerms,
        };

    private void AssertValid(AssistantOptions options) {
        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Succeeded, Is.True, result.FailureMessage);
    }

    private ValidateOptionsResult Validate(AssistantOptions options) =>
        _validator.Validate(Options.DefaultName, options);
}
