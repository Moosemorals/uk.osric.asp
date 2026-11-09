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

### Export and the status page

One exporter listens: OpenTelemetry's Prometheus exporter, registered for metrics only — no tracer
provider, no logger provider, and no instrumentation packages, because the framework's own meters are
already there (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`,
`System.Runtime`).

- **Naming is the contract.** `page.render.duration` becomes `page_render_duration_seconds_bucket`, and
  the dashboard hard-codes that. Renaming an instrument or changing its unit empties a panel. Tag
  names are sanitised the same way — the tag `page.path` is the label `page_path` — and PromQL will
  not parse the dotted form, so queries and legends use the underscore.
- **The buckets are ours.** Every duration shares one explicit scale whose top boundary is past the
  render budget, so a render recorded at its deadline lands in a real bucket rather than `+Inf`;
  `page.image.size` has its own byte scale. The SDK's default starts at 0–5 s for a seconds
  instrument, which would make every render one column and every percentile meaningless.
- **Two gates on `/metrics`.** The app publishes on the loopback only (DEC-61), and Caddy answers
  `404` for the path on the public origin. Prometheus scrapes `app:8080` over the shared rootless
  network; see [deployment](../deploy/operations.md#metrics-and-the-status-page).
- **`/status` is a shell endpoint.** It queries Prometheus over that same network and draws SVG from
  pure `Core` functions (`LineChart`, `Heatmap`, `NiceTicks`); it is owner-only, cached for fifteen
  seconds, and a Prometheus failure is a banner rather than a `500`, because the page is what gets
  opened when something is broken. The window is chosen by a `duration` token — `1h`, `3h`, `8h`,
  `1d`, `7d` or `30d`; the token is matched against that list, never parsed as a length of time, and
  anything unrecognised is the default hour. The nav shows every token, with the current one as text
  rather than a link to itself. A token sets three things: how far back the queries reach, the chart
  width (wider for a longer window) and the sample step, which widens in whole minutes so a thirty-day
  view asks for a few hundred points rather than the 43,200 a minute step would, because Prometheus
  refuses a range query over 11,000 points. Each window is cached separately, so switching back does
  not re-query. Stored data stays in seconds, but a duration axis whose whole
  range is below a second is drawn in milliseconds — `0.00025` is a worse label than `0.25`.
  The heatmap carries a legend from zero to the busiest cell and gives every cell its own value in a
  `<title>`, so a shade can be read without guessing; and "Pages slower than 0.5 s" attributes the
  slow rows to a page, which is the question the heatmap provokes. Units appear twice on purpose: in
  the title of every panel and on the vertical axis, and the heatmap's legend names the cell unit
  (`renders/s`) because its cells are rates, not counts.
- **The image carries tzdata.** Only the axis labels are local; the positions, queries and samples are
  UTC. The base is `aspnet:10.0-alpine-extra` (DEC-82) because plain alpine has no
  `/usr/share/zoneinfo` and resolving `Europe/London` inside it throws.

`make test-web` runs the Web storage tests, and `make test-integration` runs the same storage through
the real host; both are included in `make test`. Schema changes are scaffolded with
`make migration NAME=...` using the pinned local EF tool into the Web project's
`Storage/Migrations` directory. Back up the database before deploying startup migrations; see
[deployment storage and backup guidance](../deploy/operations.md#storage-and-migrations).
