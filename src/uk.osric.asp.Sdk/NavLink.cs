// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One item in a navigation landmark.</summary>
/// <remarks>
/// A page builds these, so the two strings are checked where they enter the contract rather than
/// where the document writer happens to notice.
/// </remarks>
/// <param name="Href">The URL, unencoded: the document writer encodes it.</param>
/// <param name="Text">The visible text, unencoded: the document writer encodes it.</param>
/// <param name="IsCurrent">
/// Whether this is the location being shown. A current item is text rather than a link to itself,
/// marked so assistive technology says which one it is.
/// </param>
public readonly record struct NavLink(string Href, string Text, bool IsCurrent = false) {

    /// <summary>The URL, unencoded: the document writer encodes it.</summary>
    /// <exception cref="ArgumentException">The URL is blank.</exception>
    public string Href { get; init; } = Checked(Href, nameof(Href));

    /// <summary>The visible text, unencoded: the document writer encodes it.</summary>
    /// <exception cref="ArgumentException">The text is blank.</exception>
    public string Text { get; init; } = Checked(Text, nameof(Text));

    private static string Checked(string value, string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);

        return value;
    }
}
