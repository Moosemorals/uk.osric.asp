// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>One sample in a chart series.</summary>
/// <param name="At">When the sample was taken.</param>
/// <param name="Value">The value, or null for a gap — Prometheus reports gaps as <c>NaN</c>.</param>
public readonly record struct ChartPoint(DateTimeOffset At, double? Value);
