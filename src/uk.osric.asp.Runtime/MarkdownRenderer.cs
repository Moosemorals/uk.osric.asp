// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.Tables;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using System.Collections.Concurrent;
using IMarkdownRenderer = uk.osric.asp.Sdk.IMarkdownRenderer;
using MarkdownOptions = uk.osric.asp.Sdk.MarkdownOptions;

/// <summary>The host's Markdown renderer: one author-facing profile and one restricted profile.</summary>
/// <remarks>
/// <para>
/// The page profile is CommonMark with heading anchors, pipe tables and auto-links, and raw HTML
/// passes through: <see cref="IMarkdownRenderer"/> says what that means for a page author
/// (`pages-markdown`). A page switches those three features per call with <see cref="MarkdownOptions"/>.
/// </para>
/// <para>
/// The restricted profile is for model output. Raw HTML is rendered as text, there are no autolinks
/// or images, links pass <see cref="Core.Markdown.LinkPolicy"/>, and every heading is written as
/// <c>h3</c> so a model-chosen level cannot skip one in the transcript. Both profiles use the
/// site's table renderer, which writes <c>scope="col"</c> and no inline style (`pages-markdown`).
/// </para>
/// <para>
/// The parser pipelines are built once per option set and are safe to share. Markdig's
/// <see cref="HtmlRenderer"/> is stateful, so one is created for each call.
/// </para>
/// </remarks>
public sealed class MarkdownRenderer : IMarkdownRenderer {

    private static readonly ConcurrentDictionary<MarkdownOptions, MarkdownPipeline> s_pagePipelines = new();

    private static readonly MarkdownPipeline s_restrictedPipeline = BuildRestrictedPipeline();

    /// <inheritdoc/>
    public string ToHtml(string markdown) {
        ArgumentNullException.ThrowIfNull(markdown);

        return Render(markdown, GetPagePipeline(MarkdownOptions.Default), restricted: false);
    }

    /// <inheritdoc/>
    public string ToHtml(string markdown, MarkdownOptions options) {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(options);

        return Render(markdown, GetPagePipeline(options), restricted: false);
    }

    /// <summary>Renders model output as the restricted subset the assistant transcript shows.</summary>
    /// <param name="markdown">The Markdown to render.</param>
    /// <returns>HTML that is safe to write into the assistant transcript.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is <see langword="null"/>.</exception>
    public static string ToRestrictedHtml(string markdown) {
        ArgumentNullException.ThrowIfNull(markdown);

        return Render(markdown, s_restrictedPipeline, restricted: true);
    }

    private static MarkdownPipeline GetPagePipeline(MarkdownOptions options) =>
        s_pagePipelines.GetOrAdd(options, static build => {
            MarkdownPipelineBuilder builder = new();
            if (build.HeadingAnchors) {
                // AutoIdentifiers alone writes the id; its AutoLink option would also turn
                // `[Heading]` into a link, which is a parser change a page did not ask for.
                builder.UseAutoIdentifiers(AutoIdentifierOptions.None);
            }

            if (build.AutoLinks) {
                builder.UseAutoLinks();
            }

            if (build.PipeTables) {
                builder.UsePipeTables();
            }

            return builder.Build();
        });

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
