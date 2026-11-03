// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>A named line on a chart.</summary>
/// <param name="Label">The legend text; it is escaped where it is drawn.</param>
/// <param name="Points">The samples, ordered by instant.</param>
public sealed record ChartSeries(string Label, IReadOnlyList<ChartPoint> Points);
