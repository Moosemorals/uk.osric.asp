// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>One titled drawing on the status page.</summary>
/// <param name="Panel">The catalogue entry it was drawn from.</param>
/// <param name="Svg">
/// The drawing; it is never null, because a panel with no data says so itself. Its title is the link
/// that opens the same chart on its own.
/// </param>
internal sealed record StatusPanel(Panel Panel, string Svg);

/// <summary>Everything the status endpoint needs to write the page.</summary>
/// <param name="Panels">The drawings, in catalogue order.</param>
/// <param name="Banner">Why some are missing, or null when everything answered.</param>
internal sealed record StatusPage(IReadOnlyList<StatusPanel> Panels, string? Banner);
