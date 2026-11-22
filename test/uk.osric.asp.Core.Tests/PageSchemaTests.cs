// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using uk.osric.asp.Core.Pages;

[TestFixture]
public sealed class PageSchemaTests {

    private static readonly PageSchemaLimits Limits = new(MaxTablesPerSource: 2, MaxColumnsPerTable: 3, MaxNameChars: 64);

    private static readonly string?[] Tables = [null, "my notes", "fine"];

    [Test]
    public void ASourceThatDeclaresNothingHasNothingWrongWithIt() {
        Assert.Multiple(() => {
            Assert.That(PageSchema.Empty.IsEmpty, Is.True);
            Assert.That(PageSchema.Empty.Validate(Limits), Is.Empty);
        });
    }

    [Test]
    public void ADeclarationInsideItsCapsIsApplied() {
        PageSchema schema = Schema(Table("notes", "Title", "Body"));

        Assert.That(schema.Validate(Limits), Is.Empty);
    }

    [Test]
    public void MoreTablesThanTheCapIsATableProblem() {
        PageSchema schema = Schema(Table("one", "A"), Table("two", "A"), Table("three", "A"));

        Assert.That(Messages(schema), Has.Exactly(1).Contains("at most 2 tables"));
    }

    [Test]
    public void TheSameTableNameTwiceIsRefusedWhateverTheCase() {
        PageSchema schema = Schema(Table("notes", "A"), Table("Notes", "A"));

        Assert.That(Messages(schema), Has.Some.Contains("\"Notes\" is declared more than once"));
    }

    [TestCase("my notes")]
    [TestCase("1notes")]
    [TestCase("notes-here")]
    public void ATableNameThatNeedsQuotingIsRefused(string name) {
        PageSchema schema = Schema(Table(name, "A"));

        Assert.That(Messages(schema), Has.Some.Contains("cannot be a table name"));
    }

    [Test]
    public void ATableNamedForAColumnTheHostOwnsIsRefused() {
        Assert.That(Messages(Schema(Table("Id", "A"))), Has.Some.Contains("is the name of a column the host owns"));
    }

    [Test]
    public void ATableWithNoColumnsIsRefused() {
        PageSchema schema = new([new PageTable("notes", StoreTableSubject.Page, StoreTableLifetime.Page, [])]);

        Assert.That(Messages(schema), Has.Some.Contains("declares no columns"));
    }

    [Test]
    public void MoreColumnsThanTheCapIsATableProblem() {
        PageSchema schema = Schema(Table("notes", "A", "B", "C", "D"));

        Assert.That(Messages(schema), Has.Some.Contains("at most 3 columns"));
    }

    [Test]
    public void TheSameColumnTwiceIsRefused() {
        PageSchema schema = Schema(Table("notes", "Title", "title"));

        Assert.That(Messages(schema), Has.Some.Contains("\"title\" is declared more than once"));
    }

    [Test]
    public void AColumnTheHostOwnsCannotBeDeclaredOnAVisitorsTable() {
        PageSchema visitors = Schema(new PageTable("notes", StoreTableSubject.Visitor, StoreTableLifetime.Person,
            [new PageColumn("Owner", PageColumnType.Text)]));
        PageSchema pages = Schema(Table("notes", "Owner"));

        Assert.Multiple(() => {
            Assert.That(Messages(visitors), Has.Some.Contains("a column the host owns"));
            Assert.That(pages.Validate(Limits), Is.Empty, "a page's table has no owner column, so it may declare one");
        });
    }

    [Test]
    public void AVisitorsTableCannotFollowThePage() {
        PageSchema schema = Schema(new PageTable("notes", StoreTableSubject.Visitor, StoreTableLifetime.Page,
            [new PageColumn("Title", PageColumnType.Text)]));

        Assert.That(Messages(schema), Has.Some.Contains("cannot follow the page"));
    }

    [Test]
    public void ASearchColumnTheTableDoesNotDeclareIsRefused() {
        PageSchema schema = Schema(new PageTable("notes", StoreTableSubject.Page, StoreTableLifetime.Page,
            [new PageColumn("Title", PageColumnType.Text)], Search: "Body"));

        Assert.Multiple(() => {
            Assert.That(Messages(schema), Has.Some.Contains("searches the column \"Body\""));
            Assert.That(schema.Validate(Limits), Has.Exactly(1).Matches<PageSchemaProblem>(
                problem => problem is not null && problem.Column == "Body"));
        });
    }

    [Test]
    public void EveryProblemCarriesTheTableItIsAboutOrNoneForTheSource() {
        PageSchema schema = Schema(Table("my notes", "A"), Table("fine", "B"), Table("fine", "C"));

        Assert.That(schema.Validate(Limits).Select(problem => problem.Table),
            Is.EquivalentTo(Tables), "a problem about the source names no table");
    }

    private static string[] Messages(PageSchema schema) =>
        [.. schema.Validate(Limits).Select(problem => problem.Message)];

    private static PageSchema Schema(params PageTable[] tables) => new(tables);

    private static PageTable Table(string name, params string[] columns) => new(
        name,
        StoreTableSubject.Page,
        StoreTableLifetime.Page,
        [.. columns.Select(column => new PageColumn(column, PageColumnType.Text))]);
}
