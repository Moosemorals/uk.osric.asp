// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using Microsoft.CodeAnalysis.CSharp;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;

/// <summary>The declaration a source carries, read from the symbols the compile already holds.</summary>
[TestFixture]
public sealed class PageSchemaExtractionTests {

    private static readonly PageCompiler Compiler = new();

    private static readonly string[] TwoTables = ["first", "second"];
    private static readonly string[] OneTable = ["mytable"];

    private static readonly PageColumn[] Columns = [
        new("Title", PageColumnType.Text),
        new("Body", PageColumnType.Text, Nullable: true),
        new("Words", PageColumnType.WholeNumber),
        new("Score", PageColumnType.Number),
        new("Draft", PageColumnType.Flag),
        new("Written", PageColumnType.Timestamp),
    ];

    [Test]
    public void ASourceThatDeclaresNothingHasAnEmptySchema() {
        CompileResult result = Compile("<p>Hello</p>");

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, Text(result));
            Assert.That(result.Schema.IsEmpty, Is.True);
        });
    }

    [Test]
    public void ADeclaredTableCarriesItsNameItsSubjectItsLifetimeAndItsColumns() {
        CompileResult result = Compile("""
            <%!
            using System;

            [PageTable("notes", Subject = StoreSubject.Visitor, Lifetime = StoreLifetimeKind.Session, Search = "body")]
            public sealed class Note {
                public string Title { get; set; } = "";
                public string? Body { get; set; }
                public long Words { get; set; }
                public double Score { get; set; }
                public bool Draft { get; set; }
                public DateTimeOffset Written { get; set; }
            }
            %>
            <p>notes</p>
            """);

        Assert.That(result.IsSuccess, Is.True, Text(result));
        PageTable table = result.Schema.Tables.Single();

        Assert.Multiple(() => {
            Assert.That(table.Name, Is.EqualTo("notes"));
            Assert.That(table.Subject, Is.EqualTo(StoreTableSubject.Visitor));
            Assert.That(table.Lifetime, Is.EqualTo(StoreTableLifetime.Session));
            Assert.That(table.Search, Is.EqualTo("body"));
            Assert.That(table.Columns, Is.EqualTo(Columns));
        });
    }

    [Test]
    public void ATableWithNoSubjectIsThePagesOwnAndFollowsIt() {
        CompileResult result = Compile("""
            <%!
            [PageTable("counters")]
            public sealed class Counter {
                public long Value { get; set; }
            }
            %>
            """);

        Assert.That(result.IsSuccess, Is.True, Text(result));
        PageTable table = result.Schema.Tables.Single();

        Assert.Multiple(() => {
            Assert.That(table.Subject, Is.EqualTo(StoreTableSubject.Page));
            Assert.That(table.Lifetime, Is.EqualTo(StoreTableLifetime.Page),
                "a page's table follows the page whether or not the source said so");
        });
    }

    [Test]
    public void EveryTableTheSourceDeclaresIsExtractedInTheOrderItWritesThem() {
        CompileResult result = Compile("""
            <%!
            [PageTable("first")]
            public sealed class First { public long A { get; set; } }

            [PageTable("second")]
            public sealed class Second { public long B { get; set; } }
            %>
            """);

        Assert.That(result.Schema.Tables.Select(table => table.Name), Is.EqualTo(TwoTables));
    }

    // A library owns its tables, so they are read from its own source and applied under its own key.
    [Test]
    public void ALibrariesTablesAreReadFromItsOwnSource() {
        CompileResult result = Compiler.CompileWorker(
            TemplateSourceGenerator.GenerateLibrary("""
                <%!
                namespace Notes;

                [PageTable("mytable", Subject = StoreSubject.Visitor)]
                public sealed class Declared { public long Value { get; set; } }
                %>
                """).Source!, []);

        Assert.That(result.IsSuccess, Is.True, Text(result));
        Assert.That(result.Schema.Tables.Select(table => table.Name), Is.EqualTo(OneTable));
    }

    // The page's own tree is the only one read, so a page that reaches a library's type declares
    // nothing itself and does not re-apply the library's tables under its own key.
    [Test]
    public void APageThatUsesALibraryDoesNotInheritItsDeclaration() {
        LibrarySource library = new("/_lib/notes", TemplateSourceGenerator.GenerateLibrary("""
            <%!
            namespace Notes;

            [PageTable("mytable", Subject = StoreSubject.Visitor)]
            public sealed class Declared { public long Value { get; set; } }
            %>
            """).Source!);

        CompileResult result = Compiler.Compile(
            TestTemplates.Generate("<%! using Notes; %>\n<p><%= new Declared().Value.ToString() %></p>"), [library]);

        Assert.That(result.IsSuccess, Is.True, Text(result));
        Assert.That(result.Schema.IsEmpty, Is.True);
    }

    [TestCase("public int Count { get; set; }")]
    [TestCase("public byte[] Raw { get; set; } = [];")]
    [TestCase("public long? Maybe { get; set; }")]
    [TestCase("public object Anything { get; set; } = new();")]
    public void AColumnTypeTheStoreCannotHoldIsRefused(string property) {
        CompileResult result = Compile($$"""
            <%!
            [PageTable("notes")]
            public sealed class Note { {{property}} }
            %>
            """);

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(Messages(result), Has.Some.Contains("PAGE011"));
            Assert.That(result.Schema.IsEmpty, Is.True, "a declaration with a problem is applied by nothing");
        });
    }

    [Test]
    public void AColumnWithoutASetterIsRefused() {
        CompileResult result = Compile("""
            <%!
            [PageTable("notes")]
            public sealed class Note { public string Title { get; } = ""; }
            %>
            """);

        Assert.That(Messages(result), Has.Some.Contains("PAGE011").And.Contains("setter"));
    }

    [Test]
    public void AVisitorsTableThatFollowsThePageIsRefused() {
        CompileResult result = Compile("""
            <%!
            [PageTable("notes", Subject = StoreSubject.Visitor, Lifetime = StoreLifetimeKind.Page)]
            public sealed class Note { public string Title { get; set; } = ""; }
            %>
            """);

        Assert.Multiple(() => {
            Assert.That(Messages(result), Has.Some.Contains("PAGE012").And.Contains("Person, Session or Browser"));
            Assert.That(result.Schema.IsEmpty, Is.True);
        });
    }

    [Test]
    public void APagesTableThatFollowsTheVisitorIsRefused() {
        CompileResult result = Compile("""
            <%!
            [PageTable("notes", Lifetime = StoreLifetimeKind.Person)]
            public sealed class Note { public string Title { get; set; } = ""; }
            %>
            """);

        Assert.That(Messages(result), Has.Some.Contains("PAGE012").And.Contains("StoreSubject.Visitor"));
    }

    [Test]
    public void ATableThatAsksForAOneShotLifetimeIsRefused() {
        CompileResult result = Compile("""
            <%!
            [PageTable("notes", Subject = StoreSubject.Visitor, Lifetime = StoreLifetimeKind.NextRead)]
            public sealed class Note { public string Title { get; set; } = ""; }
            %>
            """);

        Assert.That(Messages(result), Has.Some.Contains("PAGE012").And.Contains("NextRead"));
    }

    [Test]
    public void ADeclarationProblemPointsAtTheDeclarationInTheTemplate() {
        CompileResult result = Compile("""
            line one
            <%!
            [PageTable("notes")]
            public sealed class Note { public int Count { get; set; } }
            %>
            """);

        TemplatePosition? position = result.Diagnostics
            .First(diagnostic => diagnostic.Message.Contains("PAGE011", StringComparison.Ordinal)).Position;

        Assert.That(position, Is.Not.Null);
        Assert.That(position.Value.Line, Is.EqualTo(4), "the diagnostic points at the declaration, not at generated code");
    }

    private static CompileResult Compile(string template) => Compiler.Compile(TestTemplates.Generate(template), []);

    private static string[] Messages(CompileResult result) =>
        [.. result.Diagnostics.Select(diagnostic => diagnostic.Message)];

    private static string Text(CompileResult result) => string.Join(Environment.NewLine, Messages(result));
}
