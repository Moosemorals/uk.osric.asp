// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Renders the line changes between two documents as HTML for a stored page.</summary>
/// <remarks>
/// <para>
/// The renderer is reached through <see cref="IContext.Features"/> as
/// <c>ctx.Features.GetRequired&lt;IDiffRenderer&gt;()</c>, and its result is written raw:
/// <c>&lt;%== diff.ToHtml(before, after) %&gt;</c>. It supports the line changes a page can compare
/// with <see cref="IPageCatalog"/>: two templates, two revisions, or any two strings the page has.
/// </para>
/// <para>
/// <b>This is not Markdown.</b> Every source line is escaped, so a page may pass a visitor's text, a
/// fetched body or page state without turning it into markup. The result is a <c>table.diff</c> with
/// one row per changed or context line, a <c>tr.diff-hunk</c> header per hunk, and a
/// <c>div.diff-scroll</c> that scrolls sideways and takes focus; an unchanged pair writes
/// <c>p.diff-empty</c>, and a pair too different to align writes <c>p.diff-coarse</c> before the
/// table.
/// </para>
/// <para>
/// Rendering is pure: it has no I/O, needs no <c>await</c>, and is bounded by the render's body
/// allowance when the result is written. The diff itself is bounded by the host's line and edit
/// caps; past them the middle is shown as replacement blocks rather than the work being left
/// unbounded.
/// </para>
/// </remarks>
public interface IDiffRenderer {

    /// <summary>Renders a line diff of two documents as HTML.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <returns>The HTML to write with <see cref="IContext.WriteRaw"/> or <c>&lt;%== %&gt;</c>.</returns>
    /// <exception cref="ArgumentNullException">Either document is <see langword="null"/>.</exception>
    public string ToHtml(string before, string after);
}
