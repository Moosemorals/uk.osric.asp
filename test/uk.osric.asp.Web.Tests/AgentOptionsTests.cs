// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Options;
using NUnit.Framework;
using uk.osric.asp.Web.Agent;

[TestFixture]
public sealed class AgentOptionsTests {

    private readonly AgentOptionsValidator _validator = new();

    [Test]
    public void DisabledAgentDoesNotRequireProviderConfiguration() {
        AssertValid(new AgentOptions());
    }

    [Test]
    public void EnabledAgentRequiresAnApiKeyByDefaultWithoutNamingTheKeyInTheError() {
        AgentOptions options = new() { Enabled = true };

        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("Agent__ApiKey"));
        Assert.That(result.FailureMessage, Does.Not.Contain("ApiKey="));
    }

    [Test]
    public void LocalHttpProviderIsAllowedWithoutTheInsecureHttpSwitch() {
        AgentOptions options = new() {
            Enabled = true,
            RequireApiKey = false,
            BaseUrl = "http://127.0.0.1:11434/v1",
        };

        AssertValid(options);
    }

    [Test]
    public void InsecureRemoteProviderIsRejected() {
        AgentOptions options = new() {
            Enabled = true,
            RequireApiKey = false,
            BaseUrl = "http://provider.example",
        };

        Assert.That(Validate(options).FailureMessage, Does.Contain("HTTPS"));
    }

    [Test]
    public void EnabledAgentRejectsNonPositiveRunLimits() {
        AgentOptions options = new() {
            Enabled = true,
            ApiKey = "configured",
            MaxSteps = 0,
        };

        Assert.That(Validate(options).Failed, Is.True);
    }

    private void AssertValid(AgentOptions options) {
        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Succeeded, Is.True, result.FailureMessage);
    }

    private ValidateOptionsResult Validate(AgentOptions options) =>
        _validator.Validate(Options.DefaultName, options);
}
