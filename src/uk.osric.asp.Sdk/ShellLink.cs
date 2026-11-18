// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One link the shell knows how to draw, as a value rather than as markup.</summary>
/// <remarks>
/// <para>
/// <see cref="IPageCatalog.ShellLinks"/> returns these, and the page filters, orders, labels and
/// marks them itself: there is no current flag and no rendered menu, because the page owns its
/// markup. A page may also build one of its own beside the host's.
/// </para>
/// <para>
/// The two strings are checked where they enter the contract rather than where a document writer
/// happens to notice, as <see cref="NavLink"/> does.
/// </para>
/// </remarks>
/// <param name="Href">The URL, unencoded: the document writer encodes it.</param>
/// <param name="Text">The visible text, unencoded: the document writer encodes it.</param>
/// <param name="Section">Which of the shell's menus it belongs to.</param>
/// <param name="Audience">Who it is for.</param>
/// <param name="Match">
/// How a request path marks it current. A page may ignore it; a page that follows it draws the
/// same current item the shell's own menus do.
/// </param>
public readonly record struct ShellLink(
    string Href,
    string Text,
    ShellSection Section,
    ShellAudience Audience,
    ShellMatch Match = ShellMatch.Leaf) {

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
