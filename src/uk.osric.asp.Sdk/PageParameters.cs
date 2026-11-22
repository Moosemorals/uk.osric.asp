// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A statement's parameters, added by name.</summary>
/// <remarks>
/// Parameters are the only safe way to put a value into a statement, and the store cannot tell a safe
/// statement from an unsafe one: a name that reaches SQL as text is a page bug nothing refuses. A
/// builder keeps a <see cref="Dictionary{TKey,TValue}"/> of <see cref="object"/> out of page source and
/// makes the safe spelling the short one. The name may be written with or without its <c>@</c>.
/// </remarks>
public sealed class PageParameters {

    private readonly List<KeyValuePair<string, object?>> _values = [];

    /// <summary>The parameters added so far, without their <c>@</c>, in the order they were added.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Values => _values;

    /// <summary>Adds one parameter, replacing a name already added.</summary>
    /// <param name="name">The parameter's name: letters, digits and underscores, with or without a leading <c>@</c>.</param>
    /// <param name="value">The value: a string, a whole number, a number, a flag, a timestamp, bytes or null.</param>
    /// <returns>This builder, so the calls chain.</returns>
    /// <exception cref="ArgumentException">The name is empty or holds a character a parameter cannot.</exception>
    public PageParameters Add(string name, object? value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string bare = name[0] == '@' ? name[1..] : name;
        if (bare.Length == 0 || !bare.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')) {
            throw new ArgumentException(
                $"\"{name}\" cannot be a parameter name: it is letters, digits and underscores only.",
                nameof(name));
        }

        _values.RemoveAll(parameter => string.Equals(parameter.Key, bare, StringComparison.Ordinal));
        _values.Add(new(bare, value));

        return this;
    }
}
