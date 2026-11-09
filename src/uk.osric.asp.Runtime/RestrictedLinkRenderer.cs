// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;
using uk.osric.asp.Core.Markdown;

/// <summary>Renders a link only when <see cref="LinkPolicy"/> allows its URL.</summary>
/// <remarks>
/// An image is never an <c>img</c>: its alt text is written instead, so a Markdown image cannot
/// load a remote resource. A refused link keeps its text and loses its anchor, which makes a
/// hostile URL visible as the words the model wrote rather than a place to click.
/// </remarks>
internal sealed class RestrictedLinkRenderer : HtmlObjectRenderer<LinkInline> {

    private const string Relationship = "nofollow noopener noreferrer";

    protected override void Write(HtmlRenderer renderer, LinkInline link) {
        if (link.IsImage) {
            renderer.WriteChildren(link);
            return;
        }

        string? url = link.GetDynamicUrl is null ? link.Url : link.GetDynamicUrl();
        if (url is null || !LinkPolicy.IsAllowed(url)) {
            renderer.WriteChildren(link);
            return;
        }

        if (renderer.EnableHtmlForInline) {
            renderer.Write("<a href=\"");
            renderer.WriteEscapeUrl(url);
            renderer.Write('"');
            if (!string.IsNullOrEmpty(link.Title)) {
                renderer.Write(" title=\"");
                renderer.WriteEscape(link.Title!);
                renderer.Write('"');
            }
            renderer.Write(" rel=\"");
            renderer.Write(Relationship);
            renderer.Write('"');
            renderer.Write('>');
        }

        renderer.WriteChildren(link);

        if (renderer.EnableHtmlForInline) {
            renderer.Write("</a>");
        }
    }
}
