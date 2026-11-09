<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The status page, rebuilt a panel at a time

**Status:** proposed.

[OPS-06](../jobs/OPS-06.md) recorded that half of what the site measures never reaches `/status`:
sixteen of the twenty-three instruments have no panel at all. It also found the reason the gap
opened. The dashboard's panels are hand-written next to the instruments they read, in a different
file, in a different shape, with no test tying the two together — so `page.fetch.duration`,
`page.import.duration` and `page.layout.fallback` arrived and no panel noticed, and a panel naming a
metric that no longer exists draws an empty chart rather than failing a build.

The dashboard has a second problem, one no test can catch: a series is identified by a colour and
nothing else. The palette holds five colours and some panels draw more series than that, so the sixth
reuses the first; the shades are adjacent on the wheel; and the reader is colourblind.

This plan does not patch the dashboard. It **blanks it** — keeping `Core.Charting`, the Prometheus
client and the time-window machinery — and rebuilds the page from the instruments, one panel per
instrument, with the chart kind decided by the instrument's type rather than by hand. A page that
starts empty cannot keep showing the wrong thing.

## The short version

1. `/status` starts blank: `Dashboard` loses every hand-written query, and a new panel catalogue
   becomes the only way a chart can appear. The page still renders its heading, its window nav and a
   Prometheus banner.
2. **One panel per instrument.** A histogram is a heatmap, a counter is a line chart, a gauge is a
   line chart; a counter whose labels are parts of one total is a stacked area. The inventory is 23
   instruments, so there are 23 panels, and the Host group adds 5 more from the framework's meters.
3. **The chart is the panel.** Title and legend move inside the SVG, so a panel is one drawing with
   no `<h2>`, no `<ul>` legend and no prose. The page is a dense grid of `<figure>`s under three
   group headings.
4. **A series is identified without colour.** A colourblind-safe palette, a dash pattern per series,
   a direct label at the end of every line, a hover that dims the others, and a click that pins one —
   all in CSS, because `/status` stays JavaScript-free.
5. **A stacked area chart** joins `LineChart` and `Heatmap` in `Core.Charting`, for the panels where
   the labels are parts of a whole (cache lookups by result, tokens by kind, HTTP status by code).
6. **Drift becomes a failing test.** Each meter publishes its own inventory, the catalogue maps every
   entry to exactly one panel, and a test fails when either side gains an entry the other lacks or
   when a query names a family that is neither an instrument nor a known runtime one.
7. The heatmap grows a markup budget, because sixteen heatmaps at one `<rect>` per cell would be
   megabytes. Adjacent cells of the same shade merge into one rect, and columns are capped.
8. The page, its stylesheet and its docs are updated together: [metrics](../metrics.md) becomes the
   one inventory, OPS-06's copy of the table goes, and the job closes when the page is rebuilt.

## What decides a panel

The rule is mechanical, and that is the point: a reviewer can check the catalogue against the meters
without knowing what any panel is for.

| Instrument type | Chart | Why |
| --- | --- | --- |
| histogram | heatmap | A histogram *is* a distribution over buckets. A percentile line throws that away; the heatmap is the instrument, and it is what answers "is it slow, and how slow". |
| counter | line chart | A rate over time, one line per label value. |
| counter whose labels partition one total | stacked area | The parts and the whole are both readable, and the top edge of the stack is the total. |
| gauge | line chart | A level over time, one line per label value. |

Nothing is decided per panel by taste: the type comes from the instrument, and the only human choice
is whether a counter's labels are parts of a whole — which the catalogue records as a flag, with the
reason in a remark.

Three consequences worth stating before they are argued about later:

- **Quantiles are not drawn.** `histogram_quantile` over `page.render.duration` was four panels of
  the old page; under the rule it is one heatmap. The distribution is still there — a slow tail is a
  row you can point at — and no chart is invented for an instrument that already has one.
- **A heatmap aggregates every tag but `le`.** `assistant.tool.duration` merges every tool into one
  distribution, exactly as `page.render.duration` merges every path today. The panel's title says so.
  Splitting a heatmap by a tag is a small-multiples grid, and it is out of scope.
- **`page.image.size` is a histogram**, so it becomes a heatmap over its byte buckets rather than the
  old "average assembly size" line. The average hid the tail, which is the interesting part.

## The panel catalogue

`Web/Metrics/StatusCatalogue.cs` holds an ordered list of panels. Each entry names an instrument, a
chart kind, a title written for a person, a y-axis title, an empty-state sentence, and the label (if
any) the series are split by. The queries are built from the instrument name and that label, never
written by hand:

- histogram — `sum by (le) (rate(<family>_bucket[5m]))`, fed to `Heatmap.FromCumulative`.
- counter, one total — `sum(rate(<family>_total[5m]))`.
- counter, split — `sum by (<tag>) (rate(<family>_total[5m]))`.
- counter, a line with many label values — `topk(8, sum by (<tag>) (rate(<family>_total[5m])))`.
- gauge — `<family>`, or `sum by (<tag>) (<family>)`.

The five-minute rate window stays fixed, as it is today. Any shorter and a series scraped every
fifteen seconds returns samples with no rate at all; any longer and a spike inside a one-hour window is
smeared across an hour. It is longer than the step of every window, so each sample is a real rate
rather than one scrape divided by another.

### Pages (16)

| Instrument | Type | Chart | Series |
| --- | --- | --- | --- |
| `page.render.duration` | histogram | heatmap | buckets, all paths and outcomes merged |
| `page.render.wait` | histogram | heatmap | buckets |
| `page.compile.duration` | histogram | heatmap | buckets, all paths and outcomes merged |
| `page.load.duration` | histogram | heatmap | buckets |
| `page.invoke.duration` | histogram | heatmap | buckets |
| `page.save.duration` | histogram | heatmap | buckets |
| `page.fetch.duration` | histogram | heatmap | buckets, every outcome merged |
| `page.import.duration` | histogram | heatmap | buckets, every outcome merged |
| `page.image.size` | histogram | heatmap | byte buckets |
| `page.render.abandoned` | counter | line | `topk(8)` by `page_path` |
| `page.cache.lookups` | counter | stacked area | by `result` (`hit`, `stale`, `absent`) |
| `page.cache.staleness` | counter | stacked area | by `reason` |
| `page.layout.fallback` | counter | line | `topk(8)` by `page_path` |
| `page.storage.payload.size` | gauge | line | one series |
| `page.storage.database.size` | gauge | line | one series |
| `page.storage.volume.capacity` | gauge | line | one series |

The three storage gauges stay apart on purpose even though one panel would put them on a common
axis. They measure different things at different scales — a logical byte count, a file's size, a
volume's capacity — and a single axis wide enough for capacity flattens payload to the baseline. Side
by side in the grid, the comparison is still one glance.

### Assistant (7)

| Instrument | Type | Chart | Series |
| --- | --- | --- | --- |
| `assistant.run.duration` | histogram | heatmap | buckets, all models merged |
| `assistant.run.steps` | histogram | heatmap | step counts |
| `assistant.run.tool_calls` | histogram | heatmap | call counts |
| `assistant.provider.first_token.duration` | histogram | heatmap | buckets, all models merged |
| `assistant.provider.request.duration` | histogram | heatmap | buckets, all models and outcomes merged |
| `assistant.tool.duration` | histogram | heatmap | buckets, all tools merged |
| `assistant.tokens` | counter | stacked area | by `kind` (`prompt`, `completion`) |

### Host (5)

These are the framework's meters, not ours. They are not instruments this repository registers, so
they are not held to the one-panel rule — and in one place a combined panel answers one question
better than three separate ones would.

| Panel | Source families | Chart |
| --- | --- | --- |
| Process memory | `dotnet_process_memory_working_set_bytes` | line, one series |
| Process CPU | `dotnet_process_cpu_time_seconds_total` (rate) | line, one series |
| Connections and active requests | `http_server_active_requests`, `kestrel_active_connections`, `kestrel_queued_connections` | line, three series |
| Request duration | `http_server_request_duration_seconds_bucket` | heatmap |
| HTTP status mix | `http_server_request_duration_seconds_count` by `http_response_status_code` | stacked area |

Twenty-eight panels: sixteen heatmaps, eight line charts, four stacked areas.

## The chart is the panel

A panel is a `<figure>` holding one `<svg>` and nothing else. The drawing carries its own title in a
`<text class="chart-title">` band at the top and its own legend in a band beneath it: a swatch, a
dash sample and the series label, laid out in rows that wrap to the plot's width. `ChartChrome`, a
shared internal helper in `Core.Charting`, owns that band so `LineChart`, `Heatmap` and
`StackedAreaChart` cannot disagree about where the plot starts; the plot is what remains after the
title, the legend and the axes.

- The `<title>` element stays first in the SVG, because the accessibility rules require a named
  `role="img"` drawing and it is also the browser's own tooltip for the whole chart.
- The legend estimate is deterministic: a fixed per-character width, a maximum number of rows, and a
  band grown to fit. Tests pin the geometry for a known set of labels, so a layout change is a diff
  rather than a surprise.
- The page-level `<ul class="chart-legend">` and `StatusEndpoint.WriteLegendAsync` go. So does
  `StatusPanel.Labels`, and the `role="list"` allowance the accessibility rules keep for
  `chart-legend`, because the list it protected no longer exists.
- `StatusPanel.Svg` is never null. A panel that answered nothing draws its title, its axes and "No
  data in the last 1h" inside the same frame, so the grid keeps its shape and the reader learns that
  the panel exists and Prometheus had nothing for it. It is not the same statement as the banner,
  which says Prometheus itself is unwell.

Chart sizes are fixed per kind: 420×180 for a line chart, 420×200 for a stacked area and 420×280 for
a heatmap. A heatmap is the tall one because fifteen bucket labels at 11px need more height than a
plot of two or three lines, and a label that overlaps its neighbour is worse than a taller chart.
CSS scales every drawing to its grid column. `StatusDuration.ChartWidth` goes: it made a longer
window a wider chart, which a responsive grid cannot use. The window keeps choosing the query range
and the sample step, which is what actually changes the picture.

## Identifying a series without colour

Four cues, in the order the reader meets them:

1. **At rest.** Each series gets a colour *and* a dash pattern *and* a label drawn at the end of its
   line, at the last sample, inside the plot and aligned so it does not leave the drawing area. If
   two end labels would sit within a label's height of each other, the lower one is dropped and the
   legend remains. For a stacked area the label goes inside its band at the right edge where the band
   is tall enough, and only in the legend where it is not.
2. **On hover.** The series thickens and every other series in the same chart fades, so one shape can
   be followed across the chart. `svg:has(a:hover) a:not(:hover) .series { opacity: .25 }`.
3. **On click.** A series is pinned: the anchor carries the `id` its fragment names, so the anchor
   becomes the document's `:target` and the rest of the chart stays dimmed.
   `svg:has(a:target) a:not(:target) .series { opacity: .25 }`. The pin clears when another series is
   chosen or the page reloads; there is no separate "clear" control to tab through. Clicking the same
   legend entry twice keeps it pinned, which is the least surprising behaviour a fragment link can
   have without script.
4. **In the legend.** Each legend entry is a second anchor with the same fragment, so a swatch can be
   clicked as well as a 2px line, and its text is the series name.

Every anchor is `<a id="…" href="#…" tabindex="-1">` and the visible polyline, its hit stroke and its
`<title>` sit inside it, so hover and click land on the one element that owns the series. Ids are
unique for the whole document (panel slug plus series number), which `StatusEndpointTests` asserts,
because a duplicate id would pin the wrong series.

The palette becomes Okabe–Ito's colourblind-safe seven, dropping the yellow that reads as paper and
keeping the black that reads as ink: `#0072B2`, `#D55E00`, `#009E73`, `#CC79A7`, `#E69F00`,
`#56B4E9`, `#000000`. Four dash patterns cycle with them, so two series can share a colour and still
differ. `SvgText.SeriesColours` grows from five to seven and the stylesheet with it; `series-6` and
`series-7` join `series-1`..`series-5`, and `series-dash-1`..`series-dash-4` are new.

The anchors are pointer-only and the drawing stays `role="img"`. That is deliberate: a focusable
anchor inside a drawing the accessibility tree treats as one image is announced as nothing useful,
and twenty-eight charts would add up to two hundred tab stops before the reader reached the bottom of
the page. The keyboard and screen-reader affordance is the at-rest one — the end-of-line label, the
legend text and the SVG's own `<title>` — which needs no interaction at all. The pin is a convenience
for a sighted pointer, not the only way to tell two lines apart.

Colours are the last cue, not the first, and no rule in this plan depends on a reader seeing one.

## A stacked area chart

`Core/Charting/StackedAreaChart.cs`, drawing as SVG from `IReadOnlyList<ChartSeries>`, exactly as the
other two do.

- Series are stacked in label order, so the same chart is drawn the same way on every refresh —
  Prometheus does not promise an order and a band that swaps places between two views is unreadable.
- The band for a series is the area between the running total below it and the running total
  including it, filled with its series colour at low opacity and outlined on top with its series
  dash, so the top edge of each band is a line in the same style as the rest of the page.
- A missing sample is **zero**, not a gap: for the counters this chart is for, a step with no series
  means no events in that step, and a hole in the middle of a stack would break every band above it.
  The chart refuses a negative value rather than inventing a shape for it.
- The y axis is the sum of the bands; the same `NiceTicks` and `ValueUnit` rules apply, so a byte
  stack and a count stack label themselves correctly.
- The legend is inside the SVG like every other chart; a band that is too thin for an end label is
  named there only.
- `Heatmap`'s cumulation and this chart's cumulation are different operations with similar names;
  the doc comments on both say which way round they run.

## Dense packing

The page becomes three grids, one per group, under `<h2>Pages</h2>`, `<h2>Assistant</h2>` and
`<h2>Host</h2>`:

- `display: grid; grid-template-columns: repeat(auto-fill, minmax(19rem, 1fr)); gap: .5rem .75rem`,
  in `/status.css`. Three or four columns on a laptop, two on a tablet, one on a phone.
- Each `<figure>` has no margin or border of its own; the drawing's own title, legend and axes give
  it its shape. The group heading spans every column.
- The window nav stays where it is, and the banner above the grid stays as it is.
- Panels are ordered by group and, within a group, by instrument type — histograms first, then
  counters, then gauges — so like sits beside like and the reader learns the page in one visit. The
  catalogue's order is the page's order; there is no sorting at render time.

`/status.css` is a seeded page, so an existing installation keeps the old sheet until the owner
restores it from `/edit` (DEC-174). The release note in
[operations](../../deploy/operations.md#metrics-and-the-status-page) says so, and the new markup
degrades to one full-width chart per row under the old sheet rather than failing — ugly for one
deploy, never broken.

## The markup budget

Today one heatmap of a fifteen-bucket histogram over a 30-day window is fifteen rows of five hundred
cells, each a `<rect>` with a `<title>`: about seven thousand elements, for one panel. Sixteen
heatmaps of that would be a twelve-megabyte page. The charts change shape to stay inside a budget:

- **A heatmap draws runs.** Adjacent samples in the same bucket whose shade is the same merge into
  one `<rect>`, whose `<title>` names the time span and the total in that run. A typical run is a
  minute of quiet or an hour of busy, so fifteen rows become tens of rects rather than thousands, and
  a reader learns more from "0.1–0.25 s, 12:00–12:20, 37 renders" than from one cell.
- **A heatmap is capped at 120 columns.** An answer with more samples than that is drawn at the cap:
  the cumulative rows are differenced first, then neighbouring columns are summed, so a column reads
  "the renders in this period" rather than "the counter now". The x-axis still uses the window's real
  ticks.
- **A line chart keeps its five hundred points.** It needs them to show a spike, and a run of samples
  is a `<polyline>`, which is one element however many points it has. The transparent hit stroke is a
  second polyline, drawn only for charts with more than one series, where a click has to choose
  between lines.
- **A guard test pins the result.** An integration test renders `/status` against a synthetic answer
  of five hundred samples for every query and asserts the body stays under a bound chosen from the
  first measured build (expect about a megabyte). A chart change that doubles the page is then a
  failing test rather than a slow page.

## The drift guard

Three facts, and one test that ties them together:

1. **The meters publish their inventory.** `PageMetrics` and `AssistantMetrics` gain a
   `public static IReadOnlyList<Instrument> Instruments`, and construct their instruments from it, so
   a name that is registered and a name that is published cannot disagree. `Instrument` is a record of
   name, kind (histogram, counter, gauge) and unit, in `Web/Metrics`; the split tag belongs to the
   panel that reads it, not to the instrument.
2. **The catalogue maps every instrument to one panel**, and the test asserts the mapping is a
   bijection: every instrument has a panel, every panel names an instrument, and no instrument has
   two.
3. **Every family a query names is accounted for.** The test tokenises the catalogue's PromQL,
   drops the function names and keywords (`sum`, `rate`, `by`, `le`, `topk` and the rest, from a
   short list) and asserts every remaining identifier is either an instrument's family or on a
   short allow-list of runtime families (`dotnet_*`, `http_server_*`, `kestrel_*`). A renamed
   instrument fails the build instead of emptying a panel, which is the guard
   [the naming plan](archive/naming-consistency.md) said was missing.

The same test file asserts the type rule: a histogram panel's chart is a heatmap, a gauge panel's is
a line chart, and a stacked-area panel's instrument is a counter marked as parts of a whole. That is
what makes the rule in "What decides a panel" enforced rather than documented.

`PageMetricsTests` and `AssistantMetricsTests` add a listener-based check that the inventory matches
what is actually created, because a list can be published without being used.

## Components

**New**

- `src/uk.osric.asp.Core/Charting/ChartChrome.cs` — the title and legend band, and the empty state,
  shared by the three renderers.
- `src/uk.osric.asp.Core/Charting/StackedAreaChart.cs`.
- `src/uk.osric.asp.Web/Metrics/Instrument.cs` — the inventory record.
- `src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs` — the panel list, its query builder and its kinds.
- `test/uk.osric.asp.Core.Tests/StackedAreaChartTests.cs`.
- `test/uk.osric.asp.Web.Tests/StatusCatalogueTests.cs` — the drift guard and the type rule.

**Changed**

- `Core/Charting/LineChart.cs` — title and legend inside the SVG, end labels, dashes, hit strokes,
  flat plot geometry independent of the window.
- `Core/Charting/Heatmap.cs` — runs, the column cap, title and legend inside the SVG, end-of-row
  shading unchanged.
- `Core/Charting/SvgText.cs` — seven colours and the dash class.
- `Web/Metrics/Dashboard.cs` — no queries of its own; it runs the catalogue and renders each kind.
- `Web/Metrics/StatusEndpoint.cs` — `<figure>` per panel, group headings, no legend list.
- `Web/Metrics/StatusPanel.cs`, `StatusPage.cs` — no `Labels`; the SVG is never null.
- `Web/Metrics/StatusDuration.cs` — no `ChartWidth`.
- `Web/Pages/PageMetrics.cs`, `Web/Assistant/AssistantMetrics.cs` — the published inventory.
- `Web/Pages/Seeds/status.css` — the grid, the palette, the dashes, the chart title and legend.

**Unchanged**

- `Core/Charting/NiceTicks.cs`, `ValueUnit.cs`, `ChartSamples.cs`, `ChartAxis`/`ChartSize`/
  `ChartSeries`/`ChartPoint`, `PrometheusSource`, `MetricsOptions`, `StatusDuration`'s tokens and
  step rule, the 15-second page cache, and the banner behaviour. The graphing code is the part that
  works.

## Tests

- `Core.Tests` — `LineChartTests` and `HeatmapTests` gain the title band, the legend, the end labels,
  the dash classes and the run merging; `StackedAreaChartTests` pins the band order, the missing-as-
  zero rule, the negative-value refusal and the axis; `SvgTextTests` pins seven colours and the dash
  cycle; `NiceTicksTests` and `ValueUnitTests` are untouched.
- `Web.Tests` — `DashboardTests` keeps the cache, the banner and the window tests — a failed panel,
  a failed Prometheus, an empty answer and the window-to-step rule — and loses the panel-count
  assertions; `StatusCatalogueTests` is the drift guard; `StatusEndpointTests` asserts `<figure>`,
  group headings and unique ids rather than a legend list, and that a blank catalogue still renders a
  page; `PageMetricsTests` and `AssistantMetricsTests` assert the inventory against the instruments
  actually created.
- `Web.IntegrationTests` — `StatusPageTests` keeps the stylesheet and sign-in tests, drops the
  window-widens-the-chart assertion (the window sets the step now) and gains the body-size guard.
- `AccessibilityTests` — unchanged rules, re-run over the rebuilt page through `ShellViews`: every
  drawing still named, no style attributes, no skipped heading level, and the `chart-legend`
  allowance removed with the list it protected.

## Documentation and decisions

- **DEC-179** — `/status` draws one panel per instrument and the instrument's type decides the
  chart: histogram → heatmap, counter → line chart, gauge → line chart; a counter whose labels
  partition one total → stacked area. `Dashboard` holds no queries; the catalogue is the only source
  of panels.
- **DEC-180** — the page is a dense grid of `<figure>`s under three group headings, and a chart
  carries its own title and legend inside its SVG, so a panel adds no heading, no legend list and no
  prose to the document.
- **DEC-181** — a series is identified without colour: Okabe–Ito palette, a dash pattern, a direct
  end-of-line label, a hover dim and a click that pins by fragment target, all in CSS because DEC-81
  keeps `/status` JavaScript-free. The at-rest cues are the accessible ones.
- **DEC-182** — a stacked area is drawn only where the labels are parts of a whole; a missing sample
  is zero; bands are ordered by label; the axis is the sum.
- **DEC-183** — the meters publish their instruments and the catalogue maps them one-to-one, and a
  test fails when either side gains an entry the other lacks or when a query names a family that is
  neither an instrument nor a known runtime family.
- `docs/metrics.md` — the one inventory. Add the missing assistant meter table (seven instruments are
  documented nowhere today), add a "The panels" section with the instrument → chart table, and
  rewrite "Export and the status page" for the grid, the kinds and the identification rules.
- `docs/jobs/OPS-06.md` — replace the drifting inventory table with a link to `docs/metrics.md`, and
  record the three instruments that arrived after it was written. The job closes when every phase is
  green: the file is deleted and the row moves to Closed in `docs/jobs/README.md`.
- `docs/glossary.md` — "Panel" gains "exactly one instrument" and loses "one metric may appear in
  several panels", both of which the catalogue now makes checkable.
- `docs/testing.md` — the metrics paragraph names the catalogue test, the stacked-area suite and the
  body-size guard.
- `docs/architecture.md` — the `Core.Charting` row gains the third renderer.
- `deploy/operations.md` — a release note that `/status.css` must be restored from `/edit` on an
  existing installation, and one sentence on what the page now shows.
- `docs/jobs/README.md` — the OPS-06 row and its ordering entry go when the job closes, and the plan
  moves to `docs/plans/archive/` (which is frozen by its own README).

## Phases

Each phase ends with `make test` green and every phase leaves the site working. The whole job ends
with `make check`.

1. **Blank page.** Delete the fourteen hand-written panel definitions and the standalone heatmap
   query; add an empty catalogue; `Dashboard` iterates it and renders nothing; `StatusEndpoint`
   writes the heading, nav, banner and an optional panel list. Rewrite `DashboardTests`,
   `StatusEndpointTests` and the integration window test. `/status` now shows a heading and a nav —
   and that is the point.
2. **The chart is the panel.** `ChartChrome` with the title band, the legend band and the empty state;
   `LineChart` and `Heatmap` take a title and draw their legend; `StatusPanel` loses `Labels`;
   `StatusEndpoint` writes figures; `StatusDuration` loses `ChartWidth`. Tests pin the new markup.
3. **The markup budget.** Heatmap runs and the 120-column cap, with the tests that prove a busy
   answer stays small; the hit stroke rule for line charts.
4. **Identification.** Okabe–Ito palette and dash classes in `SvgText` and `status.css`, end-of-line
   labels, hover dimming, click-to-pin anchors and unique ids. A manual browser pass: hover, click,
   and the page in forced-colours mode, read as a screen reader sees one chart.
5. **Stacked area.** `StackedAreaChart` and its tests. No panel uses it yet.
6. **Inventory and catalogue.** `Instrument`, the published inventories, the catalogue's query
   builder and the drift test. Add the first panel — `page.render.duration` as a heatmap — so the
   whole path from instrument to drawing is proved before the rest are added.
7. **Pages group.** The remaining fifteen page panels.
8. **Assistant group.** The seven assistant panels, and the assistant table in `docs/metrics.md`.
9. **Host group.** The five host panels.
10. **Dense layout.** The grid in `status.css`, the group headings, and a pass over sizes and gaps at
    three widths. The body-size guard is set from this build.
11. **Finish.** Docs, decisions, the OPS-06 close-out and the archived plan.

Phases 2–5 are chart work and can be done in any order after phase 1, except that the markup budget
in phase 3 builds on the title and legend in phase 2. Phase 6 depends on 2 and 3, and 7–9 depend on 6.

## Deployment

No schema, no migration, no configuration and no new package. `/status` is owner-only and cached for
fifteen seconds; the first view of a window runs twenty-eight range queries instead of twenty, which
is the only load change. `/status.css` is seeded (DEC-174): on an existing installation the owner
restores it from `/edit`, and the release note says so. Until then the charts render full width,
one per row.

## Risks

- **`:has()` is load-bearing for the highlight.** Every browser this site is read in supports it
  (2023 onwards), and without it the page loses only the dimming — the end labels, palette, dashes and
  legend are unaffected. Worth stating in the stylesheet comment.
- **A pin survives a window change only until the page reloads**, because the nav is a link. That is
  acceptable for a page read by one person; a scripted dashboard would not accept it.
- **Sixteen heatmaps are a lot of visual weight.** If the page reads as noise in the browser pass,
  the answer is the grid's column width and the group headings, not dropping panels: one panel per
  instrument is the rule the drift guard exists to keep.
- **Stacked areas need bounded labels.** `result`, `reason`, `kind` and `http_response_status_code`
  are bounded by design; a future tag with unbounded values must not be marked as parts of a whole,
  and the catalogue comment says why.

## Out of scope

- **Derived panels.** Renders by outcome, p95 lines over the heatmaps, and a storage-headroom ratio
  were all considered. One panel per instrument is a rule a reviewer can check, and a derived panel
  is one the drift guard can no longer verify.
- **Splitting a heatmap by a tag.** One heatmap per tool, per model or per page is a small-multiples
  grid, with its own layout problem.
- **Any JavaScript.** DEC-81 keeps `/status` server-rendered and script-free; the interactions here
  are CSS or nothing.
- **More colours.** Seven is the palette; beyond that, dash and label do the work rather than an
  eighth shade nobody can tell from the seventh.
- **Grafana, a `/metrics` reader, or an alerting rule.** Those are other jobs; this one is the page.
