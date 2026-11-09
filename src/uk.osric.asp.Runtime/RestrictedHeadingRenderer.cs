// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

/// <summary>Writes every Markdown heading as the transcript's one heading level.</summary>
/// <remarks>
/// The transcript already has an <c>h2</c> naming the speaker, and a model chooses its own heading
/// depth without seeing the document it lands in. Flattening to <c>h3</c> means no heading can skip
/// a level, at the cost of the model's own hierarchy, which the text can say anyway.
/// </remarks>
internal sealed class RestrictedHeadingRenderer : HtmlObjectRenderer<HeadingBlock> {

    protected override void Write(HtmlRenderer renderer, HeadingBlock heading) {
        if (renderer.EnableHtmlForBlock) {
            renderer.Write("<h3>");
        }

        renderer.WriteLeafInline(heading);

        if (renderer.EnableHtmlForBlock) {
            renderer.Write("</h3>");
            renderer.EnsureLine();
        }
    }
}
