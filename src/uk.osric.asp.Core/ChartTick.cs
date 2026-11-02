// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>A labelled position on an axis.</summary>
/// <param name="Position">
/// The value the tick marks: seconds after the chart's first sample on a time axis, the sample value
/// itself on a value axis.
/// </param>
/// <param name="Label">The text to draw; it is escaped where it is drawn.</param>
public readonly record struct ChartTick(double Position, string Label);
