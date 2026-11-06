// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class AssistantToolArgumentsTests {

    private static readonly AssistantToolArgumentSchema[] Schema = [
        new("path", AssistantToolArgumentType.String, true),
        new("startLine", AssistantToolArgumentType.Integer, false),
        new("includeDeleted", AssistantToolArgumentType.Boolean, false),
    ];

    [Test]
    public void ReadsRequiredAndOptionalValuesFromAnObject() {
        AssistantToolArgumentsResult result = AssistantToolArguments.Parse(
            "{\"path\":\"/notes\",\"startLine\":3,\"includeDeleted\":false}", Schema, 200);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Values["path"].GetString(), Is.EqualTo("/notes"));
        Assert.That(result.Values["startLine"].GetInt32(), Is.EqualTo(3));
    }

    [Test]
    public void MissingRequiredArgumentIsRejected() {
        Assert.That(AssistantToolArguments.Parse("{}", Schema, 200).Error, Does.Contain("path"));
    }

    [Test]
    public void UnknownPropertiesAreRejected() {
        Assert.That(AssistantToolArguments.Parse("{\"path\":\"/a\",\"extra\":true}", Schema, 200).Error, Does.Contain("extra"));
    }

    [Test]
    public void WrongJsonTypesAreRejected() {
        Assert.That(AssistantToolArguments.Parse("{\"path\":4}", Schema, 200).Error, Does.Contain("string"));
    }

    [Test]
    public void ArgumentsOverTheConfiguredCharacterLimitAreRejectedBeforeParsing() {
        Assert.That(AssistantToolArguments.Parse("not-json", Schema, 3).Error, Does.Contain("limit"));
    }
}
