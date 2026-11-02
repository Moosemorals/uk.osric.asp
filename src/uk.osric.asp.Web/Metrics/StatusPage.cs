// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>Everything the status endpoint needs to write the page.</summary>
/// <param name="Panels">The charts that could be drawn.</param>
/// <param name="Banner">Why something is missing, or null when everything answered.</param>
internal sealed record StatusPage(IReadOnlyList<StatusPanel> Panels, string? Banner);
