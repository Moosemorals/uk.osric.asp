// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The slots a render offers a page that is served inside another page.</summary>
/// <remarks>
/// <para>
/// Reached through <see cref="IContext.Capabilities"/> as
/// <c>ctx.Capabilities.GetRequired&lt;IPageLayout&gt;()</c>, so <see cref="IContext"/> gains nothing for
/// chrome and a page that never wants a layout never meets one.
/// </para>
/// <para>
/// The host pre-fills <see cref="Layout"/> with the site's default, so being wrapped needs no page
/// code and opting out is one statement. A page that is wrapped must set <see cref="Title"/>: the
/// host refuses the chain before the layout runs, so the error names the page that forgot rather
/// than the layout that noticed.
/// </para>
/// <para>
/// It is not a guard. The content has already run by the time the layout starts, so a layout hides
/// chrome; it cannot prevent a side effect.
/// </para>
/// </remarks>
public interface IPageLayout {

    /// <summary>The key of the page that wraps this one, or <see langword="null"/> to be served alone.</summary>
    public string? Layout { get; set; }

    /// <summary>The document title.</summary>
    /// <exception cref="InvalidOperationException">No title has been set yet.</exception>
    public string Title { get; set; }

    /// <summary>Every layer's <see cref="AddHead"/> markup, outermost layout first.</summary>
    public string Head { get; }

    /// <summary>Every layer's sheets, in cascade order: outermost layout first, this page last.</summary>
    public IReadOnlyList<string> Stylesheets { get; }

    /// <summary>Adds raw markup to the document's <c>head</c>.</summary>
    /// <param name="markup">The markup to add; it is the caller's, not a request value.</param>
    public void AddHead(string markup);

    /// <summary>Links one stylesheet from the document.</summary>
    /// <param name="href">
    /// A site-relative URL, such as <c>/blog.css</c>. Called once per link; zero, one or many calls
    /// decide how many the document carries.
    /// </param>
    /// <exception cref="ArgumentException">The href is not a site-relative URL.</exception>
    public void AddStylesheet(string href);

    /// <summary>What the layout wraps: the body of the page inside it.</summary>
    /// <remarks>Empty in the content layer, because the layout is the only layer with one.</remarks>
    public string Body { get; }
}
