// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The Markdown features a page may switch on or off for one render.</summary>
/// <remarks>
/// <para>
/// Options belong to the call, not to the capability: the renderer is shared, so a page that changed
/// its settings would change every other page's. A page that wants a variant writes
/// <c>MarkdownOptions.Default with { HeadingAnchors = false }</c> and passes it to
/// <see cref="IMarkdownRenderer.ToHtml(string, MarkdownOptions)"/>.
/// </para>
/// <para>
/// Raw HTML always passes through. It has no option because the page template can already write it,
/// and a switch that looked like it turned HTML off would invite a page to trust the renderer with a
/// visitor's text.
/// </para>
/// </remarks>
public sealed record MarkdownOptions {

    /// <summary>The page profile: heading anchors, auto-links and pipe tables all on.</summary>
    public static MarkdownOptions Default { get; } = new();

    /// <summary>Whether each heading is written with an <c>id</c> derived from its text.</summary>
    /// <remarks>
    /// A repeated heading gets a numbered suffix, so a page can link to <c>#heading</c> and
    /// <c>#heading-1</c>. Turning this off writes a plain heading, for a page that supplies its own
    /// identifiers or holds several rendered fragments in one document.
    /// </remarks>
    public bool HeadingAnchors { get; init; } = true;

    /// <summary>Whether a bare URL becomes a link.</summary>
    public bool AutoLinks { get; init; } = true;

    /// <summary>Whether a pipe table is rendered as a table.</summary>
    public bool PipeTables { get; init; } = true;
}
