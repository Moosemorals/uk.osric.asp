<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The status page, rebuilt a panel at a time

**Status:** implemented (DEC-180–DEC-185).

[OPS-06](../../jobs/README.md#closed) recorded that half of what the site measures never reaches `/status`:
sixteen of the twenty-three instruments have no panel at all. It also found the reason the gap
opened. The dashboard's panels are hand-written next to the instruments they read, in a different
file, in a different shape, with no test tying the two together — so `page.fetch.duration`,
`page.import.duration` and `page.layout.fallback` arrived and no panel noticed, and a panel naming a
metric that no longer exists draws an empty chart rather than failing a build.

The dashboard has a second problem, one no test can catch: a series is identified by a colour and
nothing else. The palette holds five colours and some panels draw more series than that, so the sixth
reuses the first; the shades are adjacent on the wheel; and the reader is colourblind.

This plan does not patch the dashboard. It **deletes it** — keeping `Core.Charting`, the Prometheus
client and the document tools — and rebuilds the page from the instruments, one panel per
instrument, with the chart kind decided by the instrument's type rather than by hand. A page that
starts empty cannot keep showing the wrong thing, and a file that was deleted cannot keep its old
shape.

Two things it also settles:

- **The page keeps no skeleton of its own.** Every shell view is written inside
  `Html/ShellDocument`, which owns the head, the title, the sheet list, the site menu, the skip link,
  the `main` landmark and the close, and through `Sdk/PageDocument`, which owns the markup. `/status`
  therefore cannot drift from the rest of the site, and the plan adds the test that proves the view
  writes body content and nothing else. It stays a shell view rather than a stored page, because the
  error, health and status routes must answer when the page pipeline is broken (DEC-151).
- **A chart is only a drawing.** The renderer that embeds a chart in the page can serve it alone, so
  a chart can be linked directly, with the start time and duration it was drawn for. The page keeps
  embedding its SVGs; the chart endpoint is an addon, not the page's assembly line.

## The short version

1. `/status` is deleted and rebuilt. The dashboard, the endpoint, the panel records, the window type
   and their tests go in the first phase; the rewrite starts from an empty catalogue, and the
   endpoint is new code that writes body markup inside `ShellDocument` and owns no document of its
   own.
2. **One panel per instrument.** A histogram is a heatmap, a counter is a line chart, a gauge is a
   line chart; a counter whose labels are parts of one total is a stacked area. The inventory is 23
   instruments, so there are 23 panels, and the Host group adds 5 more from the framework's meters.
3. **The chart is the panel.** Title and legend move inside the SVG, so a panel is one drawing in a
   `<figure>` with no `<h2>`, no `<ul>` legend and no prose beyond its one caption link. The page is
   a dense grid of figures under three group headings.
4. **A series is identified without colour.** A colourblind-safe palette, a dash pattern per series,
   a direct label at the end of every line, a hover that dims the others, and a click that pins one.
   The interaction is CSS, because `/status` stays JavaScript-free.
5. **A drawing stands on its own.** Colour, dashes and type sizes become presentation attributes in
   `Core.Charting`, not stylesheet rules, so the same SVG is legible with no stylesheet at all.
6. **A stacked area chart** joins `LineChart` and `Heatmap` in `Core.Charting`, for the panels where
   the labels are parts of a whole (cache lookups by result, tokens by kind, HTTP status by code).
7. **One renderer, two front doors.** `ChartRenderer` turns a panel and a window — a start time and
   a duration — into an SVG. `Dashboard` embeds the result; `GET /status/chart` serves one panel's
   SVG on its own, owner-only, for a link or a bookmark.
8. **Drift becomes a failing test.** Each meter publishes its own inventory, the catalogue maps every
   entry to exactly one panel, and a test fails when either side gains an entry the other lacks or
   when a query names a family that is neither an instrument nor a known runtime one.
9. The heatmap grows a markup budget, because sixteen heatmaps at one `<rect>` per cell would be
   megabytes. Adjacent cells of the same shade merge into one rect, and columns are capped.
10. The page, its stylesheet and its docs are updated together: [metrics](../../metrics.md) becomes the
    one inventory, OPS-06's copy of the table goes, and the job closes when the page is rebuilt.

## Delete it, and keep only what is not the dashboard

The old dashboard is **deleted**, not refactored. Deciding what to keep from `Web/Metrics` line by
line costs more than rewriting the six hundred lines that hold it, and a patch that leaves a
half-kept cache or a panel record with a field nobody reads is how the next drift starts. The first
phase removes the whole surface and leaves a green tree with no dead code; everything after it adds
something that is new.

| Deleted | Why |
| --- | --- |
| `Web/Metrics/Dashboard.cs` | The hand-written panel definitions, the query list and the render helpers are the thing this plan replaces. The cache returns, rewritten and keyed by window. |
| `Web/Metrics/StatusEndpoint.cs` | Rewritten from scratch: body markup inside `ShellDocument`, figures, group headings, no chrome of its own. A rewrite is clearer than a diff through every write call. |
| `Web/Metrics/StatusPanel.cs`, `StatusPage.cs` | The old shapes carry `Title` and `Labels`, neither of which the new page has. |
| `Web/Metrics/StatusDuration.cs` | Its token table and its step rule are real knowledge, and they move into the new `ChartWindow`. The file does not survive; the decisions in it do. |
| `DashboardTests`, `StatusEndpointTests`, `StatusDurationTests` | Replaced by `StatusEndpointTests` against the new markup, `ChartEndpointTests`, `ChartWindowTests` and `StatusCatalogueTests`. Deleting the tests is what makes the rewrite honest: nothing old can quietly keep passing. |

| Kept | Why |
| --- | --- |
| `Core/Charting/*` | The graphing code. It is changed heavily — chrome, presentation attributes, runs, a third renderer — but it is not the dashboard and it works. |
| `IMetricsSource`, `QueryResult`, `PrometheusSource`, `MetricsOptions` | The Prometheus client, its DTOs and its configuration, with `PrometheusSourceTests` and `FakeMetricsSource`. None of them knows what a panel is. |
| The `/status` route, its `Editor` policy, `SecurityHeaders`, `ReservedPaths` | Response policy that belongs to the host, not to the page. Three lines of `Program.cs`, unchanged except for the chart route beside them. |
| `Html/ShellDocument`, `Sdk/PageDocument`, `Html/SiteNavigation` | The document tools of the previous review. The rewrite is what uses them properly. |

The tests go in the same commit as the code. A rewrite that leaves the old suite passing against a
shim has kept the old contract, which is the one thing this phase is for.

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

`Web/Metrics/StatusCatalogue.cs` holds an ordered list of panels. Each entry names an id, an
instrument (or, in the Host group, the runtime families it reads), a chart kind, a title written for a
person, a y-axis title, an empty-state sentence, and the label (if any) the series are split by. The
id is the instrument's name for an instrument panel and `host.memory`-style names for the Host group:
it is what `GET /status/chart?panel=…` asks for, and the drift test asserts it is unique and, where
there is an instrument, identical to the instrument's name. The queries are built from the instrument
name and the split label, never written by hand:

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

## The page and its drawings

### No skeleton of its own

`/status` is a shell view, and the shell already has one place that writes a document:
`Html/ShellDocument.BeginAsync` sets the content type, the status and `no-store`, writes the head —
title, `/layout.css`, `/site.css`, the view's own sheets, the icon — then the skip link, the banner
with `SiteNavigation.For(context)` and the open `main`; `EndAsync` closes the landmark and the
document. That is the arrangement the plan wants, and the endpoint's job is the body between the two
calls and nothing else.

The plan makes that true rather than assumed:

- The endpoint sets neither `Content-Type` nor `Cache-Control`: `ShellDocument` owns both. The old
  one set them before calling a builder that set them again, and two owners of one header is how they
  drift.
- The window nav keeps going through `PageDocument.Nav`, the builder the site menu uses too, so the
  landmark and the sheet order cannot become a hand-written copy of the chrome.
- A test renders `/status` and asserts the document around the body is exactly
  `PageDocument.Head` + `PageDocument.Banner` + `PageDocument.OpenMain` … `CloseMain` + `End` for the
  same inputs. A hand-written `<head>`, sheet list, menu or footer then fails a test instead of
  quietly diverging.
- The stylesheet list is declared once, in the `stylesheets:` argument: `/status.css` after the base
  sheets. A change to the base sheets reaches `/status` without touching it.
- It stays a shell view, not a stored page in `/_layout`: DEC-151 keeps the error, health, sign-in,
  editor and status routes answering when the page pipeline is broken, which is exactly when
  `/status` is opened.

### A panel is a figure

A panel is a `<figure>` holding one `<svg>` and, beneath it, one caption link — nothing else. The
drawing carries its own title in a `<text class="chart-title">` band at the top and its own legend in
a band beneath it: a swatch, a dash sample and the series label, laid out in rows that wrap to the
plot's width. `ChartChrome`, a shared internal helper in `Core.Charting`, owns that band so
`LineChart`, `Heatmap` and `StackedAreaChart` cannot disagree about where the plot starts; the plot is
what remains after the title, the legend and the axes.

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
CSS scales every drawing to its grid column. The old `ChartWidth`, which made a longer window a
wider chart, does not come back: a responsive grid cannot use it. The window keeps choosing the query
range and the sample step, which is what actually changes the picture.

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
differ. `SvgText.SeriesColours` grows from five to seven.

Colour, dashes, fill and type size are written as **presentation attributes** — `stroke="#0072B2"`,
`stroke-dasharray="6 4"`, `font-size="11"` — from a table in `Core.Charting`, not as rules in
`/status.css`. That is a deliberate reversal of the current arrangement: a chart served on its own has
no stylesheet, and a drawing that is invisible without one is not a drawing, it is a page fragment.
The class names stay as hooks for the two things that are page-only: the hover dim and the pinned
state, which `status.css` owns. A hotlinked chart is therefore a complete, legible, static picture
with no interaction — the interaction is a property of the page, never of the image.

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

## One renderer, two front doors

The page embeds its SVGs; it must not fetch its own endpoint to do it. So the drawing has to be a
function of a panel and a window, callable from either place:

- **`ChartWindow`** — a start and a length, plus the step the existing rule derives and the end it
  implies. It carries the six window tokens the deleted `StatusDuration` owned, so the nav's choices
  do not change, and adds the start, which the page takes as "now" and the endpoint takes from the
  request. Both snap the start down to the step, in whole minutes, so a window is a stable thing: the
  same start and length are the same chart, which is what makes a link worth keeping.
- **`ChartRenderer`** — panel + window + `IMetricsSource` → either the SVG or the reason it could not
  be drawn. This is the only code that turns a query into a drawing. `Dashboard` calls it once per
  panel; the endpoint calls it once.
- **`GET /status/chart?panel=<id>&start=<ISO-8601>&duration=<token>`** — the addon. Owner-only, the
  same `Editor` policy as `/status`, and `/status/chart` is already under the reserved `/status`
  prefix, so no page can ever claim it. The response is `image/svg+xml; charset=utf-8` and
  `Cache-Control: no-store`: it is the owner's data, and the fifteen-second cache is the server's, not
  a proxy's. The global security headers already give it `nosniff`, the frame denial and the
  script-free policy, and a drawing made of presentation attributes needs nothing relaxed.

How it behaves:

- `panel` is required; an unknown id is a `404`. `start` defaults to now and `duration` to `1h`. The
  duration accepts a window token or a positive whole number of minutes (`duration=90`), because a
  hotlink is a claim about a period, not a choice from the page's nav.
- A malformed `start` or `duration` is a `400` naming the parameter. The page's own `?duration=` stays
  lenient — an unknown token draws the default, because that is navigation typed by hand — while a
  hotlink that silently drew a different window would be a lie about evidence. Two surfaces, two
  rules, each written down.
- A Prometheus that does not answer is a `503` with a plain-text reason, not an SVG. A broken image
  is the honest failure in whatever the chart was pasted into; the page's banner is where the reason
  is read in full.
- The renderer never writes a link to itself: only the page passes a title or caption link, so a
  hotlinked chart is a picture and nothing else.
- `Dashboard`'s cache becomes a small bounded map keyed by `(start, length)` — sixteen entries,
  oldest evicted — because a start the caller chooses must not grow a dictionary without bound.

The page reaches the endpoint through each panel's one caption link: a `<figcaption>` holding an
anchor to `/status/chart?panel=…&start=…&duration=…`, labelled `Open chart` and given the panel's
title as its accessible name (`aria-label="Open chart: …"`). It is small, it is outside the SVG, it
is announced and focusable, and every one of the twenty-eight has a unique accessible name. The pin
anchors inside the drawing stay pointer-only for the reason above; this link does not.

One CSS subtlety that makes both work: a stylesheet rule beats a presentation attribute, so the page
can thicken a hovered series and dim the others without restating any colour, and the standalone
chart keeps the colours it was drawn with.

## Dense packing

The page becomes three grids, one per group, under `<h2>Pages</h2>`, `<h2>Assistant</h2>` and
`<h2>Host</h2>`:

- `display: grid; grid-template-columns: repeat(auto-fill, minmax(19rem, 1fr)); gap: .5rem .75rem`,
  in `/status.css`. Three or four columns on a laptop, two on a tablet, one on a phone.
- Each `<figure>` has no margin or border of its own; the drawing's own title, legend and axes give
  it its shape, and its caption link sits under it in the same small type. The group heading spans
  every column.
- The window nav stays where it is, and the banner above the grid stays as it is.
- Panels are ordered by group and, within a group, by instrument type — histograms first, then
  counters, then gauges — so like sits beside like and the reader learns the page in one visit. The
  catalogue's order is the page's order; there is no sorting at render time.

`/status.css` shrinks to what only the page needs: the grid, the figure and caption, the hover dim,
the pinned state, and the spacing. The palette moves into `Core.Charting` with the rest of the
presentation attributes (see "Identifying a series without colour"), so the sheet no longer defines
`--series-1`..`--series-7` or the heat shades, and a hotlinked chart does not depend on it.

`/status.css` is a seeded page, so an existing installation keeps the old sheet until the owner
restores it from `/edit` (DEC-174). The release note in
[operations](../../../deploy/operations.md#metrics-and-the-status-page) says so, and the new markup
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
  first measured build (expect about a megabyte). The chart endpoint serves the same bytes for one
  panel, so the budget covers both front doors and a hotlinked chart can never be the one that
  explodes. A chart change that doubles the page is a failing test rather than a slow page.

## The drift guard

Three facts, and one test that ties them together:

1. **The meters publish their inventory.** `PageMetrics` and `AssistantMetrics` gain a
   `public static IReadOnlyList<Instrument> Instruments`, and construct their instruments from it, so
   a name that is registered and a name that is published cannot disagree. `Instrument` is a record of
   name, kind (histogram, counter, gauge) and unit, in `Web/Metrics`; the split tag belongs to the
   panel that reads it, not to the instrument.
2. **The catalogue maps every instrument to one panel**, and the test asserts the mapping is a
   bijection: every instrument has a panel, every panel names an instrument, and no instrument has
   two. Ids are unique, and an instrument panel's id is the instrument's name, because
   `GET /status/chart?panel=…` addresses the panel by it.
3. **Every family a query names is accounted for.** The test tokenises the catalogue's PromQL,
   drops the function names and keywords (`sum`, `rate`, `by`, `le`, `topk` and the rest, from a
   short list) and asserts every remaining identifier is either an instrument's family or on a
   short allow-list of runtime families (`dotnet_*`, `http_server_*`, `kestrel_*`). A renamed
   instrument fails the build instead of emptying a panel, which is the guard
   [the naming plan](naming-consistency.md) said was missing.

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
- `src/uk.osric.asp.Web/Metrics/Dashboard.cs` — rewritten: no queries of its own, it runs the
  catalogue through `ChartRenderer` and caches by window.
- `src/uk.osric.asp.Web/Metrics/StatusEndpoint.cs` — rewritten: body markup only, inside
  `ShellDocument`; `<figure>` per panel, group headings, no legend list, no content type or cache
  header of its own.
- `src/uk.osric.asp.Web/Metrics/StatusPanel.cs`, `StatusPage.cs` — rewritten: no `Labels`, the SVG is
  never null, the caption link.
- `src/uk.osric.asp.Web/Metrics/Instrument.cs` — the inventory record.
- `src/uk.osric.asp.Web/Metrics/StatusCatalogue.cs` — the panel list, its ids, its query builder and
  its kinds.
- `src/uk.osric.asp.Web/Metrics/ChartWindow.cs` — a start, a length, the derived step and the end;
  it carries the six window tokens that `StatusDuration` held.
- `src/uk.osric.asp.Web/Metrics/ChartRenderer.cs` — panel + window + source → an SVG or the reason.
- `src/uk.osric.asp.Web/Metrics/ChartEndpoint.cs` — `GET /status/chart`.
- `test/uk.osric.asp.Core.Tests/StackedAreaChartTests.cs`.
- `test/uk.osric.asp.Web.Tests/StatusCatalogueTests.cs` — the drift guard and the type rule.
- `test/uk.osric.asp.Web.Tests/ChartEndpointTests.cs`.
- `test/uk.osric.asp.Web.Tests/ChartWindowTests.cs` — the tokens, the step rule and the snapping.

**Deleted**

- `Web/Metrics/Dashboard.cs`, `StatusEndpoint.cs`, `StatusPanel.cs`, `StatusPage.cs`,
  `StatusDuration.cs`.
- `test/uk.osric.asp.Web.Tests/DashboardTests.cs`, `StatusEndpointTests.cs`, `StatusDurationTests.cs`
  — `StatusEndpointTests` is re-created under the same name for the new code; the other two are
  replaced by `StatusCatalogueTests` and `ChartWindowTests`.

**Changed**

- `Core/Charting/LineChart.cs` — title and legend inside the SVG, end labels, dashes, presentation
  attributes, hit strokes, flat plot geometry independent of the window.
- `Core/Charting/Heatmap.cs` — runs, the column cap, title and legend inside the SVG, presentation
  attributes for the shades.
- `Core/Charting/SvgText.cs` — seven-colour palette, four dash patterns, the heat shades and the type
  sizes, all as attribute values rather than classes.
- `Web/Program.cs` — the `/status/chart` route, behind the same policy as `/status`.
- `Web/Pages/PageMetrics.cs`, `Web/Assistant/AssistantMetrics.cs` — the published inventory.
- `Web/Pages/Seeds/status.css` — the grid, the figure and caption, the hover dim; the palette and the
  dashes leave it for `Core.Charting`.

**Unchanged**

- `Core/Charting/NiceTicks.cs`, `ValueUnit.cs`, `ChartSamples.cs`, `ChartAxis`/`ChartSize`/
  `ChartSeries`/`ChartPoint`, `PrometheusSource`, `MetricsOptions`, `IMetricsSource`, `QueryResult`,
  the fifteen-second cache lifetime (`CacheFor`), the banner behaviour, `Html/ShellDocument.cs`,
  `Sdk/PageDocument.cs`, `Html/SiteNavigation.cs` and `Pages/ReservedPaths.cs` — `/status/chart` is
  already reserved by the `/status` prefix. The graphing code, the Prometheus client and the document
  tools are the parts that work.

## Tests

- `Core.Tests` — `LineChartTests` and `HeatmapTests` gain the title band, the legend, the end labels,
  the run merging and a check that a drawing carries its own `stroke`, `stroke-dasharray` and
  `font-size` and needs no stylesheet to be legible; `StackedAreaChartTests` pins the band order, the
  missing-as-zero rule, the negative-value refusal and the axis; `SvgTextTests` pins seven colours,
  the dash cycle and the heat shades; `NiceTicksTests` and `ValueUnitTests` are untouched.
- `Web.Tests` — the three dashboard fixtures are deleted in the first phase and the suite is rebuilt:
  `ChartWindowTests` pins the tokens, the step rule and the snap; `StatusCatalogueTests` is the drift
  guard; `StatusEndpointTests` asserts the document around the body is the builder's output, that
  each panel is a `<figure>` with a caption link, and that ids are unique; `ChartEndpointTests`
  asserts the content type, a `404` for an unknown panel, a `400` for a malformed window, a `503` when
  Prometheus is down, and that the bytes for a panel and window equal the ones embedded in the page;
  `PageMetricsTests` and `AssistantMetricsTests` assert the inventory against the instruments actually
  created.
- `Web.IntegrationTests` — `StatusPageTests` keeps the stylesheet and sign-in tests, drops the
  window-widens-the-chart assertion (the window sets the step now), gains the body-size guard, the
  chart route's policy (an anonymous request is redirected to sign-in, as `/status` is) and one
  end-to-end fetch of `/status/chart` from the signed-in host.
- `AccessibilityTests` — unchanged rules, re-run over the rebuilt page through `ShellViews`: every
  drawing still named, no style attributes, no skipped heading level, no unnamed caption link, and the
  `chart-legend` allowance removed with the list it protected.

## Documentation and decisions

Six decisions landed with this work (DEC-179 was taken by the browser-tests branch, so the six run
from DEC-180):

- **[DEC-180](../../decisions.md#metrics-and-the-status-page)** — one panel per instrument, and the
  type decides the chart: histogram → heatmap, counter → line chart, gauge → line chart, and a
  counter whose labels partition one total → stacked area. `Dashboard` holds no queries; the
  catalogue is the only source of panels.
- **[DEC-181](../../decisions.md#metrics-and-the-status-page)** — the page is a dense grid of
  `<figure>`s under three group headings, and a chart carries its own title and legend inside its
  SVG, so a panel adds no heading, no legend list and no prose beyond its one caption link.
- **[DEC-182](../../decisions.md#metrics-and-the-status-page)** — identification without colour:
  Okabe–Ito palette, a dash pattern, a direct end-of-line label, a hover dim and a click that pins by
  fragment target, all in CSS because DEC-81 keeps `/status` JavaScript-free. The at-rest cues are
  the accessible ones, and the series anchors are pointer-only.
- **[DEC-183](../../decisions.md#metrics-and-the-status-page)** — the inventory is the catalogue: the
  meters publish their instruments and the catalogue maps them one-to-one; a test fails when either
  side gains an entry the other lacks, when a type disagrees with the chart, or when a query names a
  family that is neither an instrument nor a known runtime one. The family names are proved against
  the live `/metrics` body.
- **[DEC-184](../../decisions.md#metrics-and-the-status-page)** — a drawing is self-contained: colour,
  dash, fill and type size are presentation attributes from a palette in `Core.Charting`, not rules
  in `/status.css`, so the same SVG is legible with no stylesheet. The page's sheet keeps only the
  grid, the caption and the hover and pinned states.
- **[DEC-185](../../decisions.md#metrics-and-the-status-page)** — a chart can be served on its own:
  `GET /status/chart?panel=&start=&duration=` returns one panel's SVG under the owner policy, with
  `no-store` and the global security headers, addressing panels by catalogue id. The window is an
  explicit start plus a length, snapped to the step, so a link reproduces the chart it showed; a
  malformed window is a `400`, an unknown panel a `404` and an unreachable Prometheus a `503`. The
  page embeds the same renderer and never fetches the endpoint.
- `docs/metrics.md` — the one inventory. Add the missing assistant meter table (seven instruments are
  documented nowhere today), add a "The panels" section with the instrument → chart table, and
  rewrite "Export and the status page" for the grid, the kinds, the identification rules and the
  chart URL with its parameters.
- `docs/jobs/OPS-06.md` — replace the drifting inventory table with a link to `docs/metrics.md`, and
  record the three instruments that arrived after it was written. The job closes when every phase is
  green: the file is deleted and the row moves to Closed in `docs/jobs/README.md`.
- `docs/glossary.md` — "Panel" gains "exactly one instrument" and loses "one metric may appear in
  several panels", both of which the catalogue now makes checkable.
- `docs/testing.md` — the metrics paragraph names the catalogue test, the stacked-area suite, the
  endpoint suite and the body-size guard.
- `docs/architecture.md` — the `Core.Charting` row gains the third renderer, and the shell/metrics
  boundary keeps its sentence about `/status` being a shell view.
- `docs/shell.md` — one sentence that `/status` writes body content only, with a link to the chart
  endpoint as the one thing on the page that is not markup it drew.
- `deploy/operations.md` — a release note that `/status.css` must be restored from `/edit` on an
  existing installation, one sentence on what the page now shows, and a hotlink example with a
  `start` and a `duration`.
- `docs/jobs/README.md` — the OPS-06 row and its ordering entry go when the job closes, and the plan
  moves to `docs/plans/archive/` (which is frozen by its own README).

## Phases

Each phase ends with `make test` green and every phase leaves the site working. The whole job ends
with `make check`.

1. **Delete it.** Remove `Dashboard`, `StatusEndpoint`, `StatusPanel`, `StatusPage` and
   `StatusDuration` from `Web/Metrics`, and `DashboardTests`, `StatusEndpointTests` and
   `StatusDurationTests` with them; keep `IMetricsSource`, `QueryResult`, `PrometheusSource`,
   `MetricsOptions`, `FakeMetricsSource`, `Core.Charting` and the document tools. The route stays,
   pointing at a new `StatusEndpoint` that writes the heading and the window nav inside
   `ShellDocument` and nothing else, and `ChartWindow` carries the six tokens and the step rule the
   deleted type held. The test that pins the document to the builder's output lands here. `/status`
   now shows the site's chrome, its heading, its window nav and no panels — and that is the point.
2. **The chart is the panel.** `ChartChrome` with the title band, the legend band and the empty state;
   `LineChart` and `Heatmap` take a title and draw their legend; the new `StatusPanel` is written with
   the caption link and no labels; `StatusEndpoint` writes figures. Tests pin the new markup.
3. **The markup budget.** Heatmap runs and the 120-column cap, with the tests that prove a busy
   answer stays small; the hit stroke rule for line charts.
4. **Identification.** Okabe–Ito palette, four dash patterns and the heat shades as presentation
   attributes in `SvgText`, `status.css` cut back to layout and interaction, end-of-line labels, hover
   dimming, click-to-pin anchors and unique ids. A manual browser pass: hover, click, the page in
   forced-colours mode, one chart opened on its own with no stylesheet, and the page read as a screen
   reader sees one chart.
5. **Stacked area.** `StackedAreaChart` and its tests. No panel uses it yet.
6. **Inventory and catalogue.** `Instrument`, the published inventories, the catalogue's ids and
   query builder, and the drift test. Add the first panel — `page.render.duration` as a heatmap — so
   the whole path from instrument to drawing is proved before the rest are added.
7. **Pages group.** The remaining fifteen page panels.
8. **Assistant group.** The seven assistant panels, and the assistant table in `docs/metrics.md`.
9. **Host group.** The five host panels.
10. **The chart endpoint.** `ChartWindow`, `ChartRenderer`, the `/status/chart` route and its tests,
    and the per-panel caption links. The page keeps embedding its SVGs, so this is the last thing
    that can be dropped without touching anything else.
11. **Dense layout.** The grid in `status.css`, the group headings, and a pass over sizes and gaps at
    three widths. The body-size guard is set from this build.
12. **Finish.** Docs, decisions, the OPS-06 close-out and the archived plan.

Phases 2–5 are chart work and can be done in any order after phase 1, except that the markup budget
in phase 3 builds on the title and legend in phase 2. Phase 6 depends on 2 and 3; 7–9 depend on 6;
10 depends on 6; 11 depends on 7–9.

## Deployment

No schema, no migration, no configuration and no new package. `/status` is owner-only and cached for
fifteen seconds; the first view of a window runs twenty-eight range queries instead of twenty, which
is the only load change. The new `/status/chart` route sits behind the same owner policy, so it is
reachable only from a signed-in browser — a hotlink is for the owner's own notes and bookmarks, not a
public embed. `/status.css` is seeded (DEC-174): on an existing installation the owner restores it
from `/edit`, and the release note says so. Until then the charts render full width, one per row.

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
- **The palette leaves the stylesheet.** A re-theme now touches `Core.Charting` rather than
  `status.css`, and the sheet's `--series-*` variables go. That is the price of a chart that stands on
  its own, and it is one of the seven decisions above; a future theme that needs per-site colours
  would pass a palette into the renderer rather than read one from CSS.
- **The hotlinked chart is not the page's chart.** It has the same marks and none of the interaction,
  and a Prometheus failure is a `503` rather than a drawn apology. Anyone pasting the URL should
  expect a picture, not a dashboard panel.
- **The chart endpoint is a second way into Prometheus.** It is owner-only and takes no PromQL — a
  panel id and a window, nothing else — so it cannot become a query console. That constraint is what
  keeps it an addon.

## Out of scope

- **Derived panels.** Renders by outcome, p95 lines over the heatmaps, and a storage-headroom ratio
  were all considered. One panel per instrument is a rule a reviewer can check, and a derived panel
  is one the drift guard can no longer verify.
- **Splitting a heatmap by a tag.** One heatmap per tool, per model or per page is a small-multiples
  grid, with its own layout problem.
- **Arbitrary PromQL or an ad-hoc query on the chart endpoint.** The endpoint addresses catalogue
  panels by id and nothing else; a query console is a different feature with a different threat
  model.
- **Sizing, theming and format parameters on the chart endpoint.** The SVG scales to whatever it is
  put in, and it is SVG only: a PNG renderer would be a second drawing pipeline.
- **Any JavaScript.** DEC-81 keeps `/status` server-rendered and script-free; the interactions here
  are CSS or nothing.
- **More colours.** Seven is the palette; beyond that, dash and label do the work rather than an
  eighth shade nobody can tell from the seventh.
- **Grafana, a `/metrics` reader, or an alerting rule.** Those are other jobs; this one is the page.
