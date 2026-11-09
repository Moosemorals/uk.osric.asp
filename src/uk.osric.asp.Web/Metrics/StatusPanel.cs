// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>One titled drawing on the status page, and the link that opens it alone.</summary>
/// <param name="Panel">The catalogue entry it was drawn from.</param>
/// <param name="Svg">The drawing; it is never null, because a panel with no data says so itself.</param>
/// <param name="Href">The chart endpoint's URL for the same panel and window.</param>
internal sealed record StatusPanel(Panel Panel, string Svg, string Href);

/// <summary>Everything the status endpoint needs to write the page.</summary>
/// <param name="Panels">The drawings, in catalogue order.</param>
/// <param name="Banner">Why some are missing, or null when everything answered.</param>
internal sealed record StatusPage(IReadOnlyList<StatusPanel> Panels, string? Banner);
