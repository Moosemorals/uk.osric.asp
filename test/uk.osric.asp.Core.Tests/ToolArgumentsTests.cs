// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class ToolArgumentsTests {

    private static readonly ToolArgumentSchema[] Schema = [
        new("path", ToolArgumentType.String, true),
        new("startLine", ToolArgumentType.Integer, false),
        new("includeDeleted", ToolArgumentType.Boolean, false),
    ];

    [Test]
    public void ReadsRequiredAndOptionalValuesFromAnObject() {
        ToolArgumentsResult result = ToolArguments.Parse(
            "{\"path\":\"/notes\",\"startLine\":3,\"includeDeleted\":false}", Schema, 200);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Values["path"].GetString(), Is.EqualTo("/notes"));
        Assert.That(result.Values["startLine"].GetInt32(), Is.EqualTo(3));
    }

    [Test]
    public void MissingRequiredArgumentIsRejected() {
        ToolArgumentsResult result = ToolArguments.Parse("{}", Schema, 200);

        Assert.That(result.Error, Does.Contain("path"));
        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public void UnknownPropertiesAreRejected() {
        Assert.That(ToolArguments.Parse("{\"path\":\"/a\",\"extra\":true}", Schema, 200).Error, Does.Contain("extra"));
    }

    [Test]
    public void WrongJsonTypesAreRejected() {
        Assert.That(ToolArguments.Parse("{\"path\":4}", Schema, 200).Error, Does.Contain("string"));
    }

    [Test]
    public void ArgumentsOverTheConfiguredCharacterLimitAreRejectedBeforeParsing() {
        Assert.That(ToolArguments.Parse("not-json", Schema, 3).Error, Does.Contain("limit"));
    }

    [Test]
    public void ArgumentsThatExactlyFillTheLimitAreParsed() {
        const string json = "{\"path\":\"/a\"}";

        Assert.That(ToolArguments.Parse(json, Schema, json.Length).Error, Is.Null);
    }

    [Test]
    public void ARootThatIsNotAnObjectIsRejected() {
        Assert.That(ToolArguments.Parse("[1]", Schema, 200).Error, Does.Contain("object"));
    }

    [Test]
    public void ARepeatedArgumentIsRejected() {
        Assert.That(ToolArguments.Parse("{\"path\":\"/a\",\"path\":\"/b\"}", Schema, 200).Error,
            Does.Contain("repeated"));
    }

    [Test]
    public void AFractionalNumberIsNotAnInteger() {
        Assert.That(ToolArguments.Parse("{\"path\":\"/a\",\"startLine\":1.5}", Schema, 200).Error,
            Does.Contain("integer"));
    }

    [Test]
    public void MissingArgumentsAreRefused() {
        Assert.That(() => ToolArguments.Parse(null!, Schema, 200), Throws.ArgumentNullException);
        Assert.That(() => ToolArguments.Parse("{}", null!, 200),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("schema"));
        Assert.That(() => ToolArguments.Parse("{}", Schema, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
