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
    public void ABudgetBelowTheFloorNamesMaxRequestBytes() {
        AssistantOptions options = new() {
            Enabled = true,
            ApiKey = "configured",
            MaxRequestBytes = 1_000,
        };

        ValidateOptionsResult result = Validate(options);

        Assert.Multiple(() => {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("Assistant:MaxRequestBytes"));
        });
    }

    private void AssertValid(AssistantOptions options) {
        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Succeeded, Is.True, result.FailureMessage);
    }

    private ValidateOptionsResult Validate(AssistantOptions options) =>
        _validator.Validate(Options.DefaultName, options);
}
