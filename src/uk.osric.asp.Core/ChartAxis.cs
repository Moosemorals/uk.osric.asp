// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>One axis of a chart: a title and the ticks drawn along it.</summary>
/// <param name="Title">What the axis measures, for the chart's description.</param>
/// <param name="Ticks">The ticks, in any order; the chart sorts them by position.</param>
public sealed record ChartAxis(string Title, IReadOnlyList<ChartTick> Ticks);
