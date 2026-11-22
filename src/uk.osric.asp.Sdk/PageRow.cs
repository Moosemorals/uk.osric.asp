// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One row a statement produced, read by column name.</summary>
/// <remarks>
/// <para>
/// A declaration is a list of names rather than a type the SQL is checked against, so a result is a row
/// of columns and not the declared type: a join's columns have no single type, and a page cannot map by
/// name through reflection. Mapping is the page's own line or two.
/// </para>
/// <para>
/// SQLite's storage classes stay out of page code. A column with no value answers
/// <see cref="IsNull"/> and a null from every getter; a column the statement did not produce is a page
/// bug and throws, with the columns the row does have in the message.
/// </para>
/// </remarks>
public sealed class PageRow {

    private readonly IReadOnlyList<string> _columns;
    private readonly IReadOnlyList<object?> _values;

    /// <summary>Creates a row from its columns and their values.</summary>
    /// <param name="columns">The column names, in the order the statement produced them.</param>
    /// <param name="values">One value per column: a string, a long, a double, a byte array, a
    /// <see cref="DateTimeOffset"/> or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">The two lists differ in length.</exception>
    public PageRow(IReadOnlyList<string> columns, IReadOnlyList<object?> values) {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);
        if (columns.Count != values.Count) {
            throw new ArgumentException(
                $"A row has one value per column, but {columns.Count} columns and {values.Count} values were given.",
                nameof(values));
        }

        _columns = columns;
        _values = values;
    }

    /// <summary>The columns the statement produced, in order.</summary>
    public IReadOnlyList<string> Columns => _columns;

    /// <summary>Whether the column holds no value.</summary>
    /// <param name="column">The column's name.</param>
    /// <returns>Whether it is null.</returns>
    /// <exception cref="ArgumentException">The statement produced no such column.</exception>
    public bool IsNull(string column) => Value(column) is null;

    /// <summary>Reads a text column.</summary>
    /// <param name="column">The column's name.</param>
    /// <returns>The text, or <see langword="null"/> when the column is null.</returns>
    /// <exception cref="ArgumentException">The statement produced no such column.</exception>
    public string? GetString(string column) => Value(column) as string;

    /// <summary>Reads a whole-number column.</summary>
    /// <param name="column">The column's name.</param>
    /// <returns>The number, or <see langword="null"/> when the column is null.</returns>
    /// <exception cref="ArgumentException">The statement produced no such column.</exception>
    /// <exception cref="InvalidCastException">The value is not a whole number.</exception>
    public long? GetInt64(string column) => Value(column) switch {
        null => null,
        long number => number,
        int number => number,
        var other => throw NotANumber(column, other, "a whole number"),
    };

    /// <summary>Reads a decimal column.</summary>
    /// <param name="column">The column's name.</param>
    /// <returns>The number, or <see langword="null"/> when the column is null.</returns>
    /// <exception cref="ArgumentException">The statement produced no such column.</exception>
    /// <exception cref="InvalidCastException">The value is not a number.</exception>
    public double? GetDouble(string column) => Value(column) switch {
        null => null,
        double number => number,
        long number => number,
        var other => throw NotANumber(column, other, "a number"),
    };

    /// <summary>Reads a column stored as <c>0</c> or <c>1</c>.</summary>
    /// <param name="column">The column's name.</param>
    /// <returns>The flag, or <see langword="null"/> when the column is null.</returns>
    /// <exception cref="ArgumentException">The statement produced no such column.</exception>
    /// <exception cref="InvalidCastException">The value is not a whole number.</exception>
    public bool? GetBoolean(string column) => GetInt64(column) switch {
        null => null,
        var number => number != 0,
    };

    /// <summary>Reads a column stored as a round-trip UTC timestamp.</summary>
    /// <param name="column">The column's name.</param>
    /// <returns>The moment, or <see langword="null"/> when the column is null.</returns>
    /// <exception cref="ArgumentException">The statement produced no such column.</exception>
    /// <exception cref="FormatException">The text is not a round-trip timestamp.</exception>
    public DateTimeOffset? GetDateTimeOffset(string column) => Value(column) switch {
        null => null,
        DateTimeOffset moment => moment,
        string text => DateTimeOffset.Parse(
            text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
        var other => throw new InvalidCastException(
            $"Column \"{column}\" is not a timestamp, so GetDateTimeOffset cannot read it: it is {other.GetType().Name}."),
    };

    private object? Value(string column) {
        ArgumentNullException.ThrowIfNull(column);

        for (int index = 0; index < _columns.Count; index++) {
            if (string.Equals(_columns[index], column, StringComparison.Ordinal)) {
                return _values[index];
            }
        }

        throw new ArgumentException(
            $"The statement produced no column called \"{column}\"; it produced {string.Join(", ", _columns.Select(name => $"\"{name}\""))}.",
            nameof(column));
    }

    private static InvalidCastException NotANumber(string column, object value, string expected) => new(
        $"Column \"{column}\" is not {expected}, so it cannot be read as one: it is {value.GetType().Name}.");
}
