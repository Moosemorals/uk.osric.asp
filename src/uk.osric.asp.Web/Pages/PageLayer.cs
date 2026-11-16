// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

/// <summary>One layer of a render: what the page runs through, its slots, and what it declared.</summary>
/// <param name="Context">The context the page runs against.</param>
/// <param name="Layout">The layer's own slots.</param>
/// <param name="Composition">The wrappers the page declared, innermost first.</param>
/// <param name="Path">
/// The chain down to and including this layer, which is what an embed or a wrap is checked against.
/// </param>
/// <param name="Depth">How many embeds sit above this layer, which is the depth cap's measure.</param>
internal sealed record PageLayer(
    SimpleContext Context,
    PageLayoutState Layout,
    PageCompositionState Composition,
    IReadOnlyList<PageRenderIdentity> Path,
    int Depth);
