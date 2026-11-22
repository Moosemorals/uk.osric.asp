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

    // A flag has no storage class of its own, and the provider's own mapping would bind it as an
    // INTEGER anyway: doing it here is what makes `Values` say what is stored.
    [Test]
    public void AFlagBecomesAWholeNumber() {
        PageParameters parameters = new PageParameters().Add("draft", true).Add("live", false);

        Assert.That(parameters.Values.Select(parameter => parameter.Value), Is.EqualTo(new object?[] { 1L, 0L }));
    }

    [TestCase(3, 3L)]
    [TestCase(3L, 3L)]
    [TestCase((short)3, 3L)]
    [TestCase((byte)3, 3L)]
    public void ANarrowerWholeNumberWidensToAWholeNumber(object value, long expected) {
        PageParameters parameters = new PageParameters().Add("n", value);

        Assert.That(parameters.Values[0].Value, Is.EqualTo(expected));
    }

    [Test]
    public void ASinglePrecisionNumberWidensToANumber() {
        PageParameters parameters = new PageParameters().Add("n", 1.5f);

        Assert.That(parameters.Values[0].Value, Is.EqualTo(1.5d));
    }

    [Test]
    public void TextBytesAndATimestampKeepTheirShape() {
        DateTimeOffset moment = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        byte[] bytes = [1, 2, 3];
        PageParameters parameters = new PageParameters().Add("t", "x").Add("b", bytes).Add("w", moment);

        Assert.Multiple(() => {
            Assert.That(parameters.Values[0].Value, Is.EqualTo("x"));
            Assert.That(parameters.Values[1].Value, Is.SameAs(bytes));
            Assert.That(parameters.Values[2].Value, Is.EqualTo(moment),
                "a timestamp is bound by the store, which holds the one spelling of its text");
        });
    }

    // The provider would take these and take them quietly: a decimal becomes a REAL, a Guid and a
    // DateTime become text shaped nothing like the column they would be compared against.
    private static readonly object[] Outside = [
        1.5m,
        Guid.Empty,
        new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
        TimeSpan.FromHours(1),
        'a',
        1ul,
    ];

    [TestCaseSource(nameof(Outside))]
    public void AValueOutsideTheSetIsRefusedByName(object value) {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new PageParameters().Add("v", value))!;

        Assert.Multiple(() => {
            Assert.That(error.Message, Does.Contain(value.GetType().Name));
            Assert.That(error.Message, Does.Contain("a string, a whole number, a number, a flag, bytes, a DateTimeOffset or null"));
        });
    }
}
