// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>Whose rows a declared table holds.</summary>
/// <remarks>
/// Core cannot reference the page contract, so this mirrors <c>StoreSubject</c> the way
/// <see cref="PageKind"/> mirrors the contract's own kind: the compiler maps one to the other, and the
/// value exists here so the schema is a plain value the store can be handed.
/// </remarks>
public enum StoreTableSubject {

    /// <summary>The page itself: every visitor sees the same rows.</summary>
    Page,

    /// <summary>One subject's rows, behind an owner column the page never writes.</summary>
    Visitor,
}

/// <summary>How long a visitor's rows in a declared table live.</summary>
/// <remarks>
/// The contract's lifetime has <c>Default</c> and <c>NextRead</c> in it because it is one enum; neither
/// is a table's, so neither is here. A page's table follows the page and ignores this.
/// </remarks>
public enum StoreTableLifetime {

    /// <summary>Dies with the page.</summary>
    Page,

    /// <summary>Dies with the account; anonymously, when the session does unless adoption moves the rows.</summary>
    Person,

    /// <summary>Dies when the visit ends.</summary>
    Session,

    /// <summary>Dies when the browser cookie goes.</summary>
    Browser,
}

/// <summary>The storage class one declared column holds.</summary>
public enum PageColumnType {

    /// <summary>Text, which is also what a timestamp is stored as.</summary>
    Text,

    /// <summary>A whole number.</summary>
    WholeNumber,

    /// <summary>
    /// A number.
    /// </summary>
    Number,

    /// <summary><c>0</c> or <c>1</c>.</summary>
    Flag,

    /// <summary>A round-trip UTC timestamp, stored as text so a comparison is a string comparison.</summary>
    Timestamp,
}

/// <summary>One declared column.</summary>
/// <param name="Name">The property's name, which is the column's.</param>
/// <param name="Type">The storage class it holds.</param>
/// <param name="Nullable">Whether the declaration allows no value.</param>
public sealed record PageColumn(string Name, PageColumnType Type, bool Nullable = false);

/// <summary>One declared table.</summary>
/// <param name="Name">The name the source writes in SQL.</param>
/// <param name="Subject">Whose rows it holds.</param>
/// <param name="Lifetime">How long a visitor's rows live; ignored for a page's table.</param>
/// <param name="Columns">The columns, in declaration order.</param>
/// <param name="Search">The one column a search matches, or <see langword="null"/>.</param>
public sealed record PageTable(
    string Name,
    StoreTableSubject Subject,
    StoreTableLifetime Lifetime,
    IReadOnlyList<PageColumn> Columns,
    string? Search = null);

/// <summary>What one stored source declares, as the compiler extracted it.</summary>
/// <remarks>
/// A schema is part of the revision, not a second document: the declaration in the source is the
/// table, a save applies the difference between this value and what was last applied, and a revision
/// that does not compile applies nothing. The value is plain, so a store, a test and a saved revision
/// all hold the same thing.
/// </remarks>
public sealed record PageSchema {

    /// <summary>A source that declares no tables.</summary>
    public static PageSchema Empty { get; } = new([]);

    /// <summary>Creates a schema from its tables, in declaration order.</summary>
    /// <param name="tables">The declared tables.</param>
    /// <exception cref="ArgumentNullException">No list was given.</exception>
    public PageSchema(IReadOnlyList<PageTable> tables) {
        ArgumentNullException.ThrowIfNull(tables);
        Tables = tables;
    }

    /// <summary>The declared tables, in the order the source declares them.</summary>
    public IReadOnlyList<PageTable> Tables { get; }

    /// <summary>Whether the source declares nothing, so a save has no data to apply.</summary>
    public bool IsEmpty => Tables.Count == 0;

    /// <summary>
    /// The problems that make a declaration one the host cannot apply.
    /// </summary>
    /// <param name="limits">The caps a declaration obeys.</param>
    /// <returns>One problem per table or column, each naming what to change.</returns>
    /// <exception cref="ArgumentNullException">No limits were given.</exception>
    /// <remarks>
    /// These are <c>PAGE012</c>: a declaration the host cannot apply stores the revision, reports the
    /// problem at the declaration and leaves the schema and its rows untouched. The compiler answers
    /// the two questions only symbols can settle — a column type it cannot store, and a name another
    /// source already holds — and asks this value about the rest.
    /// </remarks>
    public IReadOnlyList<PageSchemaProblem> Validate(PageSchemaLimits limits) {
        ArgumentNullException.ThrowIfNull(limits);

        List<PageSchemaProblem> problems = [];
        if (Tables.Count > limits.MaxTablesPerSource) {
            problems.Add(new(null, null,
                $"a source declares at most {limits.MaxTablesPerSource} tables, but this one declares {Tables.Count}."));
        }

        HashSet<string> names = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (PageTable table in Tables) {
            if (!names.Add(table.Name)) {
                problems.Add(new(table.Name, null, $"the table name \"{table.Name}\" is declared more than once."));
            }

            problems.AddRange(CheckTable(table, limits));
        }

        return problems;
    }

    private static IEnumerable<PageSchemaProblem> CheckTable(PageTable table, PageSchemaLimits limits) {
        if (!IsIdentifier(table.Name) || table.Name.Length > limits.MaxNameChars) {
            yield return new(table.Name, null,
                $"\"{table.Name}\" cannot be a table name: it is 1 to {limits.MaxNameChars} letters, digits and underscores, and does not start with a digit.");
        }

        if (string.Equals(table.Name, "Id", StringComparison.OrdinalIgnoreCase)
            || (table.Subject is StoreTableSubject.Visitor && string.Equals(table.Name, "Owner", StringComparison.OrdinalIgnoreCase))) {
            yield return new(table.Name, null,
                $"\"{table.Name}\" is the name of a column the host owns, so it cannot be a table's.");
        }

        if (table.Lifetime is StoreTableLifetime.Page && table.Subject is StoreTableSubject.Visitor) {
            yield return new(table.Name, null,
                "a visitor's table cannot follow the page: choose Person, Session or Browser.");
        }

        if (table.Columns.Count is 0) {
            yield return new(table.Name, null, $"\"{table.Name}\" declares no columns.");
        }

        if (table.Columns.Count > limits.MaxColumnsPerTable) {
            yield return new(table.Name, null,
                $"a table declares at most {limits.MaxColumnsPerTable} columns, but \"{table.Name}\" declares {table.Columns.Count}.");
        }

        HashSet<string> columns = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (PageColumn column in table.Columns) {
            if (!columns.Add(column.Name)) {
                yield return new(table.Name, column.Name,
                    $"the column name \"{column.Name}\" is declared more than once on \"{table.Name}\".");
            }

            if (!IsIdentifier(column.Name) || column.Name.Length > limits.MaxNameChars) {
                yield return new(table.Name, column.Name,
                    $"\"{column.Name}\" cannot be a column name: it is 1 to {limits.MaxNameChars} letters, digits and underscores, and does not start with a digit.");
            }

            if (string.Equals(column.Name, "Id", StringComparison.OrdinalIgnoreCase)
                || (table.Subject is StoreTableSubject.Visitor && string.Equals(column.Name, "Owner", StringComparison.OrdinalIgnoreCase))) {
                yield return new(table.Name, column.Name,
                    $"\"{column.Name}\" is a column the host owns, so a declaration cannot add one.");
            }
        }

        if (table.Search is { } search
            && !table.Columns.Any(column => string.Equals(column.Name, search, StringComparison.Ordinal))) {
            yield return new(table.Name, search,
                $"\"{table.Name}\" searches the column \"{search}\", which it does not declare.");
        }
    }

    private static bool IsIdentifier(string name) {
        if (name.Length is 0 || char.IsAsciiDigit(name[0])) {
            return false;
        }

        return name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}

/// <summary>The caps a declaration obeys.</summary>
/// <param name="MaxTablesPerSource">The most tables one source may declare.</param>
/// <param name="MaxColumnsPerTable">The most columns one table may declare.</param>
/// <param name="MaxNameChars">The longest table or column name.</param>
public sealed record PageSchemaLimits(int MaxTablesPerSource, int MaxColumnsPerTable, int MaxNameChars);

/// <summary>One reason a declaration cannot be applied.</summary>
/// <param name="Table">The table it is about, or <see langword="null"/> when it is about the source.</param>
/// <param name="Column">The column it is about, or <see langword="null"/> when it is about the table.</param>
/// <param name="Message">What is wrong, and what to write instead.</param>
public sealed record PageSchemaProblem(string? Table, string? Column, string Message);
