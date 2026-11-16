// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What a rendered page contributes outside its body.</summary>
/// <remarks>
/// <para>
/// This is the finished material of one layer, not the live <see cref="IPageLayout"/> a layer runs
/// with: it can be carried, folded into the caller's own slots, or discarded. A page that embeds
/// another page receives this from <see cref="IPageComposition.EmbedAsync"/>, where the body is
/// already written; a wrapper receives the layer inside it as a <see cref="PageCapture"/>.
/// </para>
/// <para>
/// Nothing is merged behind the outer page's back. The page that owns the document decides what to
/// fold, with <see cref="IPageLayout.AddHead"/> and <see cref="IPageLayout.AddStylesheet"/> on its own
/// slots, so an abstract frame and an abstract body are all a composition ever hands over.
/// </para>
/// </remarks>
/// <param name="Title">The outermost layer's title, or <see langword="null"/> when it set none.</param>
/// <param name="Head">The outermost layer's <see cref="IPageLayout.AddHead"/> markup.</param>
/// <param name="Stylesheets">The outermost layer's sheets, in the order it added them.</param>
/// <param name="AllowsScripts">
/// Whether the page, or any layer it is wrapped in, asked to load same-origin scripts.
/// </param>
public record PageFrame(
    string? Title,
    string Head,
    IReadOnlyList<string> Stylesheets,
    bool AllowsScripts);
