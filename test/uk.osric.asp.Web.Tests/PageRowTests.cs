// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Sdk;

[TestFixture]
public sealed class PageRowTests {

    private static readonly string[] Columns = ["Title", "Written"];

    [Test]
    public void AColumnIsReadByItsNameInAnyOrder() {
        PageRow row = new(["Title", "Written"], ["Notes", null]);

        Assert.Multiple(() => {
            Assert.That(row.GetString("Title"), Is.EqualTo("Notes"));
            Assert.That(row.IsNull("Written"), Is.True);
            Assert.That(row.Columns, Is.EqualTo(Columns));
        });
    }

    [Test]
    public void AColumnTheStatementDidNotProduceThrowsAndNamesTheOnesItDid() {
        PageRow row = new(["Title", "Body"], ["Notes", ""]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => row.GetString("Titl"))!;

        Assert.That(error.Message, Does.Contain("\"Titl\"").And.Contain("\"Title\", \"Body\""));
    }

    [Test]
    public void ANullColumnAnswersNullRatherThanZero() {
        PageRow row = new(["n", "d", "b", "w"], [null, null, null, null]);

        Assert.Multiple(() => {
            Assert.That(row.GetInt64("n"), Is.Null);
            Assert.That(row.GetDouble("d"), Is.Null);
            Assert.That(row.GetBoolean("b"), Is.Null);
            Assert.That(row.GetDateTimeOffset("w"), Is.Null);
        });
    }

    [Test]
    public void AFlagIsAWholeNumber() {
        PageRow row = new(["yes", "no"], [1L, 0L]);

        Assert.Multiple(() => {
            Assert.That(row.GetBoolean("yes"), Is.True);
            Assert.That(row.GetBoolean("no"), Is.False);
        });
    }

    [Test]
    public void ATimestampIsReadFromItsRoundTripText() {
        PageRow row = new(["written"], ["2026-11-20T10:00:00.1230000Z"]);

        Assert.That(row.GetDateTimeOffset("written"), Is.EqualTo(new DateTimeOffset(2026, 11, 20, 10, 0, 0, 123, TimeSpan.Zero)));
    }

    [Test]
    public void AColumnOfTheWrongShapeThrowsRatherThanAnsweringAProbableValue() {
        PageRow row = new(["title"], ["Notes"]);

        Assert.That(() => row.GetInt64("title"), Throws.TypeOf<InvalidCastException>());
    }

    [Test]
    public void ARowHoldsOneValuePerColumn() {
        Assert.That(() => new PageRow(["a", "b"], ["one"]), Throws.ArgumentException);
    }
}
