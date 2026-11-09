// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using IMarkdownRenderer = uk.osric.asp.Sdk.IMarkdownRenderer;

/// <summary>The host's Markdown renderer: one author-facing profile and one restricted profile.</summary>
/// <remarks>
/// <para>
/// The page profile is CommonMark with pipe tables and auto-links, and raw HTML passes through. It
/// is the author's renderer, not a sanitiser: a page author can already write HTML in the template
/// and with <c>&lt;%== %&gt;</c> (DEC-163).
/// </para>
/// <para>
/// The restricted profile is for model output. Raw HTML is rendered as text, there are no autolinks
/// or images, links pass <see cref="Core.Markdown.LinkPolicy"/>, and every heading is written as
/// <c>h3</c> so a model-chosen level cannot skip one in the transcript. Both profiles use the
/// site's table renderer, which writes <c>scope="col"</c> and no inline style (DEC-164).
/// </para>
/// <para>
/// The parser pipelines are built once and are safe to share. Markdig's <see cref="HtmlRenderer"/>
/// is stateful, so one is created for each call.
/// </para>
/// </remarks>
public sealed class MarkdownRenderer : IMarkdownRenderer {

    private static readonly MarkdownPipeline s_pagePipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .Build();

    private static readonly MarkdownPipeline s_restrictedPipeline = BuildRestrictedPipeline();

    /// <inheritdoc/>
    public string ToHtml(string markdown) {
        ArgumentNullException.ThrowIfNull(markdown);

        return Render(markdown, s_pagePipeline, restricted: false);
    }

    /// <summary>Renders model output as the restricted subset the assistant transcript shows.</summary>
    /// <param name="markdown">The Markdown to render.</param>
    /// <returns>HTML that is safe to write into the assistant transcript.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public string ToRestrictedHtml(string markdown) {
        ArgumentNullException.ThrowIfNull(markdown);

        return Render(markdown, s_restrictedPipeline, restricted: true);
    }

    private static MarkdownPipeline BuildRestrictedPipeline() {
        MarkdownPipelineBuilder builder = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UsePipeTables();

        // A bare URL or <https://…> would otherwise become a link the restricted renderer never
        // sees, so the parser must not make one in the first place.
        builder.InlineParsers.TryRemove<AutolinkInlineParser>();

        return builder.Build();
    }

    private static string Render(string markdown, MarkdownPipeline pipeline, bool restricted) {
        MarkdownDocument document = Markdown.Parse(markdown, pipeline);
        using StringWriter writer = new();
        HtmlRenderer renderer = new(writer) {
            EnableHtmlForBlock = true,
            EnableHtmlForInline = true,
            EnableHtmlEscape = true,
        };

        // The table renderer is installed by the extension's renderer setup, so the pipeline must
        // set the renderer up before the site's renderer replaces it.
        pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<HtmlTableRenderer>(new AccessibleTableRenderer());
        if (restricted) {
            renderer.ObjectRenderers.Replace<LinkInlineRenderer>(new RestrictedLinkRenderer());
            renderer.ObjectRenderers.Replace<HeadingRenderer>(new RestrictedHeadingRenderer());
        }

        renderer.Render(document);
        return writer.ToString();
    }
}
