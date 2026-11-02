// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>One row of a heatmap: a bucket label and its samples.</summary>
/// <param name="Label">The bucket boundary, for example <c>0.05</c> or <c>+Inf</c>.</param>
/// <param name="Points">The samples; a null value is a gap.</param>
public sealed record HeatmapRow(string Label, IReadOnlyList<ChartPoint> Points);
