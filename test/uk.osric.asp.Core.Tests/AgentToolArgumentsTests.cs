// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Agent;

[TestFixture]
public sealed class AgentToolArgumentsTests {

    private static readonly AgentToolArgumentSchema[] Schema = [
        new("path", AgentToolArgumentType.String, true),
        new("startLine", AgentToolArgumentType.Integer, false),
        new("includeDeleted", AgentToolArgumentType.Boolean, false),
    ];

    [Test]
    public void ReadsRequiredAndOptionalValuesFromAnObject() {
        AgentToolArgumentsResult result = AgentToolArguments.Parse(
            "{\"path\":\"/notes\",\"startLine\":3,\"includeDeleted\":false}", Schema, 200);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Values["path"].GetString(), Is.EqualTo("/notes"));
        Assert.That(result.Values["startLine"].GetInt32(), Is.EqualTo(3));
    }

    [Test]
    public void MissingRequiredArgumentIsRejected() {
        Assert.That(AgentToolArguments.Parse("{}", Schema, 200).Error, Does.Contain("path"));
    }

    [Test]
    public void UnknownPropertiesAreRejected() {
        Assert.That(AgentToolArguments.Parse("{\"path\":\"/a\",\"extra\":true}", Schema, 200).Error, Does.Contain("extra"));
    }

    [Test]
    public void WrongJsonTypesAreRejected() {
        Assert.That(AgentToolArguments.Parse("{\"path\":4}", Schema, 200).Error, Does.Contain("string"));
    }

    [Test]
    public void ArgumentsOverTheConfiguredCharacterLimitAreRejectedBeforeParsing() {
        Assert.That(AgentToolArguments.Parse("not-json", Schema, 3).Error, Does.Contain("limit"));
    }
}
