// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Metrics;

/// <summary>One titled chart on the status page.</summary>
/// <param name="Title">The heading, written for a person.</param>
/// <param name="Svg">The chart, or null when the window held no samples.</param>
/// <param name="Labels">The series labels, in the order the chart colours them.</param>
internal sealed record StatusPanel(string Title, string? Svg, IReadOnlyList<string> Labels);
