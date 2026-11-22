// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A statement's parameters, added by name.</summary>
/// <remarks>
/// <para>
/// Parameters are the only safe way to put a value into a statement, and the store cannot tell a safe
/// statement from an unsafe one: a name that reaches SQL as text is a page bug nothing refuses. A
/// builder keeps a <see cref="Dictionary{TKey,TValue}"/> of <see cref="object"/> out of page source and
/// makes the safe spelling the short one. The name may be written with or without its <c>@</c>.
/// </para>
/// <para>
/// A value is narrowed to what a page may bind — a string, a whole number, a number, a flag, bytes, a
/// <see cref="DateTimeOffset"/> or nothing — and added in the form the store binds. The provider would
/// take more than that, and take it quietly: a <c>decimal</c> would become a REAL and lose digits, and
/// a <c>DateTime</c> a string shaped nothing like the text a timestamp column holds. So a value outside
/// the set is refused here, where the page can see which one it passed.
/// </para>
/// </remarks>
public sealed class PageParameters {

    private readonly List<KeyValuePair<string, object?>> _values = [];

    /// <summary>
    /// The parameters added so far, without their <c>@</c>, in the order they were added: a string, a
    /// whole number, a number, bytes, a <see cref="DateTimeOffset"/> or <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Values => _values;

    /// <summary>Adds one parameter, replacing a name already added.</summary>
    /// <param name="name">The parameter's name: letters, digits and underscores, with or without a leading <c>@</c>.</param>
    /// <param name="value">
    /// The value: a string, a whole number, a number, a flag, bytes, a <see cref="DateTimeOffset"/> or
    /// <see langword="null"/>. A flag is added as <c>1</c> or <c>0</c> and a narrower number as a whole
    /// number or a number, so what the store binds is always one of those six.
    /// </param>
    /// <returns>This builder, so the calls chain.</returns>
    /// <exception cref="ArgumentException">
    /// The name is empty or holds a character a parameter cannot; or the value is not one of the six a
    /// page may bind.
    /// </exception>
    public PageParameters Add(string name, object? value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string bare = name[0] == '@' ? name[1..] : name;
        if (bare.Length == 0 || !bare.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')) {
            throw new ArgumentException(
                $"\"{name}\" cannot be a parameter name: it is letters, digits and underscores only.",
                nameof(name));
        }

        _values.RemoveAll(parameter => string.Equals(parameter.Key, bare, StringComparison.Ordinal));
        _values.Add(new(bare, Normalize(name, value)));

        return this;
    }

    // SQLite has four storage classes, and these are the values that reach one without losing
    // anything on the way. The order matters only where a pattern's type is a subtype of another's.
    private static object? Normalize(string name, object? value) => value switch {
        null => null,
        string text => text,
        long or double or byte[] or DateTimeOffset => value,
        bool flag => flag ? 1L : 0L,
        int number => (long)number,
        short number => (long)number,
        sbyte number => (long)number,
        byte number => (long)number,
        ushort number => (long)number,
        uint number => (long)number,
        float number => (double)number,
        _ => throw new ArgumentException(
            $"\"{name}\" cannot be bound as a {value.GetType().Name}: a parameter is a string, a whole number, a number, a flag, bytes, a DateTimeOffset or null.",
            nameof(value)),
    };
}
