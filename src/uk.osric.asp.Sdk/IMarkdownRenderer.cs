// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Renders Markdown to HTML for a stored page.</summary>
/// <remarks>
/// <para>
/// The renderer is reached through <see cref="IContext.Capabilities"/> as
/// <c>ctx.Capabilities.GetRequired&lt;IMarkdownRenderer&gt;()</c>, and its result is written raw:
/// <c>&lt;%== markdown.ToHtml(source) %&gt;</c>. It supports CommonMark, pipe tables, auto-links
/// and the links and images Markdown can express. Each heading carries an <c>id</c> derived from its
/// text, so a page can link to a section; <see cref="MarkdownOptions"/> switches that, auto-links
/// and pipe tables per call.
/// </para>
/// <para>
/// <b>This is not a sanitiser.</b> Raw HTML in the Markdown passes through, exactly like
/// <see cref="IContext.WriteRaw"/>, and a link's scheme is not restricted. A page author may write
/// HTML directly, so that is no more authority than the template already has — but text from a
/// visitor, a fetch or storage is data, not markup. Escape it with <see cref="IContext.Write"/>
/// rather than rendering it.
/// </para>
/// <para>
/// Rendering is pure: it has no I/O, needs no <c>await</c>, and is bounded by the render's body
/// allowance when the result is written.
/// </para>
/// </remarks>
public interface IMarkdownRenderer {

    /// <summary>Renders a Markdown document to HTML with the page profile.</summary>
    /// <param name="markdown">The Markdown to render. Raw HTML in it is passed through.</param>
    /// <returns>The HTML to write with <see cref="IContext.WriteRaw"/> or <c>&lt;%== %&gt;</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public string ToHtml(string markdown);

    /// <summary>Renders a Markdown document to HTML with the features the page chooses.</summary>
    /// <param name="markdown">The Markdown to render. Raw HTML in it is passed through.</param>
    /// <param name="options">The features to switch on or off for this render.</param>
    /// <returns>The HTML to write with <see cref="IContext.WriteRaw"/> or <c>&lt;%== %&gt;</c>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="markdown"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public string ToHtml(string markdown, MarkdownOptions options);
}
