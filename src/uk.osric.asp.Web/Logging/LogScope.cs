// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Logging;

using System.Collections;

/// <summary>The ambient facts every line written while the scope is open carries.</summary>
/// <remarks>
/// <para>
/// The formatter reads the fields when the entry is written, not when the scope is opened, so a value
/// that only becomes known later - the signed-in account, a route value - is on every line after it
/// and on no line before it, which is the truth.
/// </para>
/// <para>
/// The keys are the schema of the journal: anything naming something ends in <c>Id</c>, and the
/// signed-in account is <c>UserName</c>. See docs/logging.md.
/// </para>
/// </remarks>
internal sealed class LogScope : IEnumerable<KeyValuePair<string, object?>> {

    private readonly Dictionary<string, object?> _fields = [];

    /// <summary>Adds or replaces a field; a value that is not known adds nothing.</summary>
    /// <param name="name">The field's name, as it appears in the journal.</param>
    /// <param name="value">The value, or <see langword="null"/> when it is not known.</param>
    public LogScope Set(string name, object? value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // An empty string is a value that was read and had nothing in it, which is not the same as a
        // fact we do not have: the journal keeps the one and omits the other.
        if (value is not null && (value is not string text || text.Length > 0)) {
            _fields[name] = value;
        }

        return this;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _fields.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // The formatter writes the scope's text as the scope's message. A type name would be noise in
    // every line; the fields are the message.
    public override string ToString() => string.Empty;
}
