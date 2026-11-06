<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# OPS-06 — Half of what the site measures, the dashboard never shows

| | |
| --- | --- |
| Severity | Low |
| Area | Operations, observability |
| Location | `src/uk.osric.asp.Web/Metrics/Dashboard.cs`, `Pages/PageMetrics.cs`, `Assistant/AssistantMetrics.cs` |

## Problem

The site records twenty instruments across two meters. The dashboard renders fourteen panels plus a
heatmap, and between them they reach ten of those twenty.

| Meter | Instrument | Shown |
| --- | --- | --- |
| `uk.osric.asp.Pages` | `page.render.duration` | yes — four panels |
| | `page.render.wait` | yes |
| | `page.compile.duration` | yes |
| | `page.render.abandoned` | yes |
| | `page.cache.lookups` | yes |
| | `page.cache.staleness` | yes |
| | `page.image.size` | yes, mean only |
| | `page.load.duration` | **no** |
| | `page.invoke.duration` | **no** |
| | `page.save.duration` | **no** |
| | `page.storage.payload.size` | **no** |
| | `page.storage.database.size` | **no** |
| | `page.storage.volume.capacity` | **no** |
| `uk.osric.asp.Assistant` | `assistant.run.duration` | **no** |
| | `assistant.run.steps` | **no** |
| | `assistant.run.tool_calls` | **no** |
| | `assistant.provider.first_token.duration` | **no** |
| | `assistant.provider.request.duration` | **no** |
| | `assistant.tool.duration` | **no** |
| | `assistant.tokens` | **no** |

The absent half is not the boring half. The three storage gauges are the only early warning that the
`uk.osric.asp-storage` volume is filling — the failure mode this deployment actually has, because
every page row carries a compiled assembly. `page.save.duration` and the handler load/invoke pair
cover the editor's slow path and the per-request cost that a compile-then-serve hides. And the whole
assistant vertical is invisible: it is the most expensive thing the site does, it calls an external
provider, and nothing on `/status` mentions it.

An instrument nobody displays is a cost with no payoff: tag cardinality on a per-path label, series
count in a 512 MB Prometheus, and code that has to be kept working. The awkward middle is worse than
either end — a metric kept "just in case" is one nobody has checked.

The reverse failure matters too. A panel whose PromQL names a metric that no longer exists renders as
an empty chart, not an error, so drift is silent. `DashboardTests` pins one query out of about
twenty (`page_cache_lookups_total`, as a fake's predicate) and no test ties the instrument names the
app registers to the metric families the panels ask for.

## Fix outline

1. For each of the twenty instruments, decide: a panel, or stop recording. The table above is the
   inventory; the decision only needs to survive being read in a year.
2. Add the three panels that earn their place first:
   - **Storage growth** — payload, database and volume capacity on one panel, because the interesting
     number is the ratio and the trend, not any single gauge.
   - **Page assembly lifecycle** — `load`, `invoke` and `save` durations beside the render duration
     already shown, so a slow request can be attributed to compiling, loading, running or saving.
   - **Assistant cost** — runs, steps, tool calls, tokens and provider first-token latency. A separate
     panel because it answers a different question from the request path.
3. Make drift loud: a test that every instrument registered by `PageMetrics` and `AssistantMetrics`
   appears in at least one `PromQL` query, and that every metric family named in a query is either a
   registered instrument or a known runtime one (`dotnet_*`, `http_server_*`, `kestrel_*`). That is
   the guard that turns a silent empty panel into a failing build, and it is what makes renaming a
   metric safe in future.
4. Give any new histogram an `AddView` in `Program.cs` at the same time — a missing one falls back to
   the generic OpenTelemetry bounds without saying so.
5. Keep one panel to one question. The dashboard is read cold, under pressure, by one person who has
   forgotten what it says.

This job is separate from [the naming plan](../plans/naming-consistency.md), which renames a meter and
two instruments while the Prometheus history is disposable. Doing that work first is easier: it moves
the names once, before there are panels to update.
