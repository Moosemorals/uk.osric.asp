<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Metrics and the dashboard

What the site measures, how the instruments are named, and how `/status` reads them back. Logs are
the other half of the same question and live in [logging](logging.md).

## Instrumentation

`PageMetrics` owns a `Meter` named `uk.osric.asp.Pages` and records:

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `page.render.duration` | histogram | s | `page.path` (`(not-found)` for a miss), `outcome` (`ok`, `compile-error`, `error`, `not-found`, `timeout`), `cache` (`hit`, `miss`, `none`) |
| `page.render.wait` | histogram | s | — time spent waiting for a render slot; site-wide, not per page |
| `page.render.abandoned` | counter | — | `page.path` — renders given up on at the deadline |
| `page.compile.duration` | histogram | s | `page.path`, `outcome`, `reason` (`save`, `lazy`) |
| `page.load.duration` | histogram | s | `page.path` |
| `page.invoke.duration` | histogram | s | `page.path`, `outcome` |
| `page.save.duration` | histogram | s | `page.path`, `outcome` |
| `page.fetch.duration` | histogram | s | `page.path`, `outcome` (`ok`, `http-error`, `not-allowed`, `timeout`, `network`, `invalid`, `limit`, `redirect-limit`, `aborted`) |
| `page.image.size` | histogram | By | `page.path` |
| `page.import.duration` | histogram | s | `outcome` — one imported archive, compiled and committed |
| `page.layout.fallback` | counter | — | `page.path` — pages served without the default layout because it could not be served |
| `page.storage.payload.size` | observable gauge | By | — logical UTF-8 bytes for every revision text column plus image/symbol blobs, and the `PageState` key and value bytes; excludes indexes and SQLite overhead |
| `page.storage.database.size` | observable gauge | By | — whole database `page_count * page_size`, including indexes and freelist pages; excludes the separate WAL |
| `page.storage.volume.capacity` | observable gauge | By | — total capacity of the filesystem volume containing the database |
| `page.cache.lookups` | counter | — | `result` (`hit`, `stale`, `absent`) |
| `page.cache.staleness` | counter | — | `page.path`, `reason` (`never-compiled`, `template-changed`, `runtime-changed`) |

`AssistantMetrics` owns a `Meter` named `uk.osric.asp.Assistant` and records:

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `assistant.run.duration` | histogram | s | `outcome`, `model` |
| `assistant.run.steps` | histogram | {step} | `outcome` — how many steps one run took |
| `assistant.run.tool_calls` | histogram | {call} | `outcome` — how many tool calls one run made |
| `assistant.provider.first_token.duration` | histogram | s | `model` — the wait before the model said anything |
| `assistant.provider.request.duration` | histogram | s | `model`, `outcome` |
| `assistant.tool.duration` | histogram | s | `tool` (a registered tool's name, or `(unknown)`), `outcome` |
| `assistant.tokens` | counter | {token} | `kind` (`prompt`, `completion`) |

A miss is tagged `page.path="(not-found)"`. The requested path is unauthenticated and unbounded, so
tagging it verbatim would let a scan mint one time series per URL; the real path is in the access log.
The wait histogram carries no path tag for the same reason, and because queueing is a property of the
site rather than of a page.

Durations are seconds to match OpenTelemetry and ASP.NET Core's own hosting instruments, so do not
add a request-duration metric of our own. On a cache miss `page.compile.duration` is a subset of
`page.render.duration`; never add the two together. `page.path` is bounded by the number of stored
pages, which is small — if that ever stops being true, drop the tag and keep the path in logs.
Recording costs nothing until an exporter listens, so the instruments are always on. For a quick
look while the site runs: `dotnet-counters monitor --counters uk.osric.asp.Pages <pid>`.

The bundled SQLite build does not provide `dbstat`, so exact allocated bytes for an individual table
and its indexes/overflow pages are not available without adding a dependency. The payload gauge is
explicitly logical size, not allocated table size. `PageStorageSampler` samples at startup and about
every 60 seconds, and a scrub requests a coalesced refresh. It uses a short-lived database scope,
bounded SQLite commands and cancellation; observable gauge callbacks return the last cached sample
without database or filesystem I/O. Failed samples are logged and retain the prior values. Compare
logical payload with volume capacity only as an operational estimate; use the whole-database gauge
for database growth. Review retention manually when payload exceeds half the volume capacity; no
automatic pruning is performed.

## The panels

The dashboard is built from the instruments rather than written beside them: one panel per
instrument, and the instrument's type decides the chart (DEC-180).

| Instrument | Chart | Why |
| --- | --- | --- |
| histogram | heatmap | A histogram is a distribution over buckets; a percentile line throws that away, and a heatmap answers "is it slow, and how slow". |
| counter | line chart | A rate over time, one line per label value. |
| counter whose labels partition one total | stacked area | The parts and the whole are readable, and the top edge of the stack is the total. |
| gauge | line chart | A level over time. |

`StatusCatalogue` holds the list: sixteen Pages panels (nine heatmaps for the duration and size
histograms, stacked areas for cache lookups and staleness, line charts for abandoned renders, layout
fallbacks and the three storage gauges), seven Assistant panels (six heatmaps and the token counter
stacked by `kind`), and five Host panels from the framework's own meters — process memory, process
CPU, connections and active requests, request duration and the HTTP status mix. The Host panels are
not instruments this repository registers, so a combined panel there is allowed where three series
answer one question.

A heatmap aggregates every tag but `le`: `assistant.tool.duration` merges every tool into one
distribution, exactly as `page.render.duration` merges every path. The panel's title says so, and
splitting a heatmap by a tag would be a grid of small multiples rather than one chart.

The page is a grid of `<figure>`s under three headings, and each drawing carries its own title and
legend inside its SVG (DEC-181). The only thing the page adds is one caption link per figure, which
opens that chart on its own. A panel whose answer was empty draws its own "No data in this window."
rather than a gap in the grid, which is a different statement from the banner that says Prometheus
itself is unwell.

**The inventory is the catalogue.** Each meter publishes its instruments, the catalogue maps them
one-to-one, and tests fail when either side gains an entry the other lacks, when a type disagrees
with the chart, or when a query names a family that is neither an instrument nor a known runtime one
(DEC-183). `MetricNamesTests` records every instrument and reads the live `/metrics` body, so a
renamed instrument or a changed unit fails a test rather than emptying a panel.

**A heatmap draws runs, not cells.** Neighbouring samples of the same shade merge into one `rect` with
one hover title, and an answer with more samples than the cap allows is folded into at most sixty
columns before it is drawn. Sixteen heatmaps of a busy thirty-day window then fit in the page's
markup budget, which `StatusPageSizeTests` pins.

### Identifying a series without colour

Each series gets a colour from the Okabe–Ito palette, a dash pattern, and a label drawn at the end of
its line; the legend repeats both inside the drawing. Hovering or focusing a series dims the others,
and clicking it pins that highlight by SVG fragment target (DEC-182). The interaction is CSS only,
because `/status` is script-free (DEC-81), and the at-rest cues — the end-of-line label, the dash and
the legend — are the accessible ones. The series anchors are pointer-only; a keyboard or screen-reader
reader gets the label, the legend and the drawing's own `<title>`.

The palette and the dashes are presentation attributes written by `Core.Charting`, not stylesheet
rules, so a chart served on its own is legible with no stylesheet at all (DEC-184). `/status.css`
holds only the grid, the caption and the hover and pinned states.

## One chart on its own

Any panel can be linked directly (DEC-185):

```
GET /status/chart?panel=page.render.duration&start=2026-11-09T12:00:00Z&duration=1h
```

- `panel` is a catalogue id: the instrument's name, or a `host.` id for a Host panel.
- `start` is an ISO-8601 instant, defaulting to now; `duration` is a window token (`1h`, `3h`, `8h`,
  `1d`, `7d`, `30d`) or a whole number of minutes, defaulting to `1h`.
- The window is snapped to the sample step, so the same start and duration draw the same picture and
  a link reproduces what it showed.
- The response is `image/svg+xml; charset=utf-8` with `Cache-Control: no-store`, under the same owner
  policy as `/status` and the global security headers. It is the owner's data, and the fifteen-second
  cache is the server's rather than a proxy's.
- A malformed window is a `400` naming the parameter, an unknown panel a `404`, and a Prometheus that
  does not answer a `503` with the reason as plain text. A broken image is the honest failure for a
  pasted link; the page's banner is where the reason is read in full.

The page embeds the same renderer and never fetches this route, so the endpoint can be removed
without touching the page. The route is already covered by the reserved `/status` prefix, so no stored
page can claim it.

## Export and the status page

One exporter listens: OpenTelemetry's Prometheus exporter, registered for metrics only — no tracer
provider, no logger provider, and no instrumentation packages, because the framework's own meters are
already there (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`,
`System.Runtime`).

- **Naming is the contract.** `page.render.duration` becomes `page_render_duration_seconds_bucket`, and
  the catalogue hard-codes that. Renaming an instrument or changing its unit empties a panel, which is
  what `MetricNamesTests` and the family test exist to catch. Tag names are sanitised the same way —
  the tag `page.path` is the label `page_path` — and PromQL will not parse the dotted form, so queries
  and legends use the underscore.
- **The buckets are ours.** Every duration shares one explicit scale whose top boundary is past the
  render budget, so a render recorded at its deadline lands in a real bucket rather than `+Inf`;
  `page.image.size` has its own byte scale. The SDK's default starts at 0–5 s for a seconds
  instrument, which would make every render one column and every percentile meaningless.
- **Two gates on `/metrics`.** The app publishes on the loopback only (DEC-61), and Caddy answers
  `404` for the path on the public origin. Prometheus scrapes `app:8080` over the shared rootless
  network; see [deployment](../deploy/operations.md#metrics-and-the-status-page).
- **`/status` is a shell endpoint.** It writes body markup inside `ShellDocument`, so its head, sheet
  list, menu and footer are the shell's (see [the shell](shell.md)). It queries Prometheus over the
  shared network and draws SVG from pure `Core` functions (`LineChart`, `Heatmap`, `StackedAreaChart`,
  `NiceTicks`); it is owner-only, cached for fifteen seconds, and a Prometheus failure is a banner
  rather than a `500`, because the page is what gets opened when something is broken. The window is
  chosen by a `duration` token — `1h`, `3h`, `8h`, `1d`, `7d` or `30d`; the token is matched against
  that list, never parsed as a length of time, and anything unrecognised is the default hour. The nav
  shows every token, with the current one as text rather than a link to itself. A token sets two
  things: how far back the queries reach and the sample step, which widens in whole minutes so a
  thirty-day view asks for a few hundred points rather than the 43,200 a minute step would, because
  Prometheus refuses a range query over 11,000 points. Each window is cached separately, in a bounded
  map, so switching back does not re-query and a link cannot grow it without limit.
- **The image carries tzdata.** Only the axis labels are local; the positions, queries and samples are
  UTC. The base is `aspnet:10.0-alpine-extra` (DEC-82) because plain alpine has no
  `/usr/share/zoneinfo` and resolving `Europe/London` inside it throws.

`/status.css` is a seeded page like every other sheet, so an installation that already has it keeps
the old rules until the owner restores it from the page list. `make test-web` runs the Web storage
tests, and `make test-integration` runs the same storage through the real host; both are included in
`make test`. Schema changes are scaffolded with `make migration NAME=...` using the pinned local EF
tool into the Web project's `Storage/Migrations` directory. Back up the database before deploying
startup migrations; see [deployment storage and backup guidance](../deploy/operations.md#storage-and-migrations).
