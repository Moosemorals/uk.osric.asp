// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

/// <summary>A value axis after it has been rescaled into a readable unit.</summary>
/// <param name="Min">The lowest value, in <paramref name="Unit"/>.</param>
/// <param name="Max">The highest value, in <paramref name="Unit"/>.</param>
/// <param name="Unit">The unit the values are expressed in, for the axis title.</param>
public readonly record struct ChartScale(double Min, double Max, string Unit);
