// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The slots one layer of a render owns, and the layer inside it.</summary>
/// <remarks>
/// <para>
/// Reached through <see cref="IContext.Capabilities"/> as
/// <c>ctx.Capabilities.GetRequired&lt;IPageLayout&gt;()</c>, so <see cref="IContext"/> gains nothing for
/// chrome and a page that never wants a layout never meets one.
/// </para>
/// <para>
/// The slots are this layer's own: the host does not fold the layer inside into them. A wrapper reads
/// <see cref="Content"/> and decides what to fold with <see cref="AddHead"/>, <see cref="AddStylesheet"/>
/// and <see cref="Title"/>, so the page that owns the document chooses in both directions.
/// </para>
/// <para>
/// A page is wrapped only if it asks: <see cref="IPageComposition.Wrap()"/> names the site's layout,
/// and a page that asks for nothing is served exactly as it wrote itself.
/// </para>
/// <para>
/// It is not a guard. The content has already run by the time the wrapper starts, so a layout hides
/// chrome; it cannot prevent a side effect.
/// </para>
/// <para>
/// A page that streams its response with <see cref="IPageResponse.FlushAsync"/> is served alone: a
/// layout needs the whole body before it can write its own, so a page that flushes is a page that
/// declares no wrapper.
/// </para>
/// </remarks>
public interface IPageLayout {

    /// <summary>The layer inside this one, when this is a wrapper; <see langword="null"/> otherwise.</summary>
    /// <remarks>
    /// The content's whole body, wrappers and all, beside the frame of the outermost layer inside
    /// this one. A wrapper that ignores it serves its own document and nothing of the content.
    /// </remarks>
    public PageCapture? Content { get; }

    /// <summary>This layer's own document title, or <see langword="null"/> until it is set.</summary>
    /// <remarks>
    /// The setter refuses a blank title, so the only way to have none is to leave it unset, and a
    /// wrapper decides what a missing title from the layer inside it means.
    /// </remarks>
    /// <exception cref="ArgumentException">The title is blank.</exception>
    public string? Title { get; set; }

    /// <summary>This layer's <see cref="AddHead"/> markup, in the order it was added.</summary>
    public string Head { get; }

    /// <summary>This layer's sheets, in the order it added them.</summary>
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
}
