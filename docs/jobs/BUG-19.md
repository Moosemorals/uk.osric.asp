<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-19 — A chart draws its line from the left edge, not at its samples' instants

| | |
| --- | --- |
| Severity | Medium |
| Area | Core charting, metrics dashboard |
| Location | `src/uk.osric.asp.Core/Charting/LineChart.cs`, `StackedAreaChart.cs`, `Heatmap.cs`, `ChartSamples.cs`, `src/uk.osric.asp.Web/Metrics/ChartRenderer.cs` |

## Problem

A chart draws correctly only when its samples reach the left edge of its window. Given a window wider
than the data — an hour of samples in a day's window, which is what a quiet metric, a fresh instrument
or a signal that started late gives — the line is drawn across the left of the plot from the first
sample, and the ticks beneath it name instants the data does not sit at.

The cause is that the chart and its axis count from different origins. `ChartRenderer` builds the time
axis with `NiceTicks.ForTime(window.Start, window.End, …)`, whose positions are seconds after the
**window's** start, while `LineChart.TimeRange` takes its origin from the **earliest sample**
(`ChartSamples.Instants`) and starts the plot there. The ticks carry no origin, so a chart cannot
recover the one they were counted from; `ChartSamples.TimeSpan` only widens the span to reach the last
tick, which is why the bug is invisible while the data fills the window and why an empty answer still
gets a plausible axis.

Two other charts have the same fault in a different form. `StackedAreaChart` also offsets every
column from `timeline[0].At`, and `Heatmap` does not use instants at all: a cell is
`plot.Width / columns` wide from `plot.Left`, so a bucket map is stretched across the whole window
whatever part of it its rows cover. When the data does reach past the end of the window
`ChartPlot.Time` clamps the extra points onto the right edge, which hides the overshoot rather than
showing it.

## Fix outline

- Make the axis the origin as well as the ticks: carry the window on the axis (a start instant beside
  the ticks, or a `ChartWindow` value the charts take) and place a sample at
  `(point.At - from) / (to - from)`. The ticks are already relative to `from`, so the grid, the line
  and the labels then agree by construction rather than by coincidence. `ChartAxis` is public in
  `Core`, so the shape — a new member or a value beside it — is the decision to make first.
- Drop the guess in `ChartSamples.TimeSpan`: with the axis owning the span, a sample set can no
  longer stretch it, and an empty answer keeps the axis the window asked for.
- Clip samples outside the window instead of clamping them onto its edge, so an overshoot reads as
  one rather than as a flat line pinned to the right.
- Place a heatmap cell at its bucket's instant and width over the same axis, so its columns and the
  time labels it already draws are measured the same way.
- Tests, one per chart, with a window wider than the samples: assert a known sample's `x`/`cx` is at
  its fraction of the plot, and a sample past the end is clipped rather than moved to `Right`. They
  belong in `LineChartTests`, `StackedAreaChartTests` and the heatmap's cases in
  `ChartConsistencyTests`; `ChartWindowTests` already covers the window the renderer passes.
