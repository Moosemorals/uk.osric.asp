// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;

[TestFixture]
public sealed class PageParametersTests {

    [Test]
    public void AParameterKeepsItsNameWithoutTheMarker() {
        PageParameters parameters = new PageParameters().Add("@since", 3L).Add("title", "Notes");

        Assert.That(parameters.Values, Is.EqualTo(new KeyValuePair<string, object?>[] {
            new("since", 3L),
            new("title", "Notes"),
        }));
    }

    [Test]
    public void AddingANameASecondTimeReplacesTheValue() {
        PageParameters parameters = new PageParameters().Add("since", 1L).Add("since", 2L);

        Assert.That(parameters.Values, Is.EqualTo(new KeyValuePair<string, object?>[] { new("since", 2L) }));
    }

    [Test]
    public void ANullIsAParameterLikeAnyOther() {
        PageParameters parameters = new PageParameters().Add("gone", null);

        Assert.That(parameters.Values, Has.Exactly(1).Matches<KeyValuePair<string, object?>>(parameter => parameter.Value is null));
    }

    [TestCase("")]
    [TestCase("@")]
    [TestCase("a name")]
    [TestCase("a-name")]
    public void ANameThatCannotBeAParameterIsRefused(string name) {
        Assert.That(() => new PageParameters().Add(name, 1L), Throws.ArgumentException);
    }
}
